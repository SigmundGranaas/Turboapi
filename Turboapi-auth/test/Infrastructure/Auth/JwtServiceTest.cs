using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Turboapi.Domain.Aggregates;
using Turboapi.Infrastructure.Auth;
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;
using Turboapi.Infrastructure.Tests.Persistence;
using Turboapi.Integration.Tests.Messaging;
using Xunit;
using Xunit.Abstractions;

namespace Turboapi.Infrastructure.Tests.Auth
{
    [Collection("PostgresContainerCollection")]
    public class JwtServiceTests : IDisposable
    {
        private readonly PostgresContainerFixture _fixture;
        private readonly AuthDbContext _dbContext;
        private readonly RefreshTokenRepository _refreshTokenRepository;
        private readonly JwtConfig _jwtConfig;
        private readonly JwtService _jwtService;

        public JwtServiceTests(PostgresContainerFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _dbContext = _fixture.CreateContext(); 

            CleanupDatabase();

            _refreshTokenRepository = new RefreshTokenRepository(_dbContext);

            _jwtConfig = new JwtConfig
            {
                Key = "TestSuperSecretKeyMinimumLengthForHS256AlgorithmIsActually32BytesSoThisIsSufficient",
                Issuer = "test-issuer",
                Audience = "test-audience",
                TokenExpirationMinutes = 5,
                RefreshTokenExpirationDays = 1
            };
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddMXLogger(output).SetMinimumLevel(LogLevel.Debug));
            var mockLoggerJwtService = loggerFactory.CreateLogger<JwtService>();

            _jwtService = new JwtService(
                Options.Create(_jwtConfig),
                _refreshTokenRepository,
                mockLoggerJwtService
            );
        }

        private void CleanupDatabase()
        {
            _dbContext.RefreshTokens.RemoveRange(_dbContext.RefreshTokens);
            _dbContext.Accounts.RemoveRange(_dbContext.Accounts);
            _dbContext.SaveChanges();
            _dbContext.ChangeTracker.Clear();
        }

        private async Task<Account> SeedAccountAsync(Guid? id = null, string? email = null, IEnumerable<string>? roleNames = null)
        {
            var accountId = id ?? Guid.NewGuid();
            var accountEmail = email ?? $"test-{accountId}@example.com";
            var accountRoles = roleNames?.ToList() ?? new List<string> { "User" };
            
            var account = Account.Create(accountId, accountEmail, accountRoles);
            
            _dbContext.Accounts.Add(account);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            return account;
        }

        [Fact]
        public async Task GenerateTokensAsync_ShouldCreateValidJwtAndPersistRefreshToken()
        {
            // Arrange
            var account = await SeedAccountAsync(roleNames: new[] { "User", "Admin" });

            // Act
            var tokenResult = await _jwtService.GenerateTokensAsync(account);
            await _dbContext.SaveChangesAsync();

            // Assert Access Token
            Assert.NotNull(tokenResult.AccessToken);
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(tokenResult.AccessToken) as JwtSecurityToken;
            Assert.NotNull(jsonToken);
            Assert.Equal(_jwtConfig.Issuer, jsonToken.Issuer);
            Assert.Equal(_jwtConfig.Audience, jsonToken.Audiences.First());
            Assert.Equal(account.Id.ToString(), jsonToken.Subject);
            Assert.Equal(account.Email, jsonToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
            Assert.Contains(jsonToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
            Assert.Contains(jsonToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
            Assert.True(jsonToken.ValidTo > DateTime.UtcNow);

            // Assert Refresh Token in DB
            Assert.NotNull(tokenResult.RefreshToken);
            var persistedRefreshToken = await _dbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == tokenResult.RefreshToken && rt.AccountId == account.Id);
            Assert.NotNull(persistedRefreshToken);
            Assert.False(persistedRefreshToken.IsRevoked);
            Assert.True(persistedRefreshToken.ExpiresAt > DateTime.UtcNow);
            Assert.Equal(account.Id, tokenResult.AccountId);
        }

        [Fact]
        public async Task GenerateNewTokenStringsAsync_ShouldReturnValidStringsAndCorrectExpiry_WithoutPersistence()
        {
            // Arrange
            var account = await SeedAccountAsync();

            // Act
            var result = await _jwtService.GenerateNewTokenStringsAsync(account);
            var dbCountBeforeSave = await _dbContext.RefreshTokens.CountAsync();
            await _dbContext.SaveChangesAsync(); // Try to save, nothing should happen
            var dbCountAfterSave = await _dbContext.RefreshTokens.CountAsync();

            // Assert
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
            Assert.False(string.IsNullOrWhiteSpace(result.RefreshTokenValue));

            // Check Access Token
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(result.AccessToken) as JwtSecurityToken;
            Assert.NotNull(jsonToken);
            Assert.Equal(account.Id.ToString(), jsonToken.Subject);

            // Check Refresh Token Expiry
            var expectedExpiry = DateTime.UtcNow.AddDays(_jwtConfig.RefreshTokenExpirationDays);
            Assert.True((expectedExpiry - result.RefreshTokenExpiresAt).TotalSeconds < 5); // Allow a small tolerance

            // Ensure no DB changes were made
            Assert.Equal(0, dbCountBeforeSave);
            Assert.Equal(0, dbCountAfterSave);
        }

        [Fact]
        public async Task ValidateAccessTokenAsync_ShouldSucceedForValidToken()
        {
            // Arrange
            var account = await SeedAccountAsync();
            var tokenResult = await _jwtService.GenerateTokensAsync(account); 
            await _dbContext.SaveChangesAsync();

            // Act
            var principal = await _jwtService.ValidateAccessTokenAsync(tokenResult.AccessToken);

            // Assert
            Assert.NotNull(principal);
            var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub) ?? principal.FindFirst(ClaimTypes.NameIdentifier);
            Assert.NotNull(subClaim);
            Assert.Equal(account.Id.ToString(), subClaim.Value);
        }

        [Fact]
        public async Task ValidateAccessTokenAsync_ShouldFailForInvalidTokenSignature()
        {
            // Arrange
            var differentKeyConfig = new JwtConfig 
            { 
                Key = "DifferentSecretKeyThatIsAlsoVeryLongAndSecureEnoughForTestingPurposes123", 
                Issuer = _jwtConfig.Issuer, 
                Audience = _jwtConfig.Audience, 
                TokenExpirationMinutes = 5 
            };
            var tempServiceWithDifferentKey = new JwtService(
                Options.Create(differentKeyConfig), 
                _refreshTokenRepository, 
                new LoggerFactory().CreateLogger<JwtService>()
            );
            
            var account = await SeedAccountAsync();
            var tokenResultFromDifferentKey = await tempServiceWithDifferentKey.GenerateTokensAsync(account);

            // Act
            var principal = await _jwtService.ValidateAccessTokenAsync(tokenResultFromDifferentKey.AccessToken);

            // Assert
            Assert.Null(principal);
        }

        [Fact]
        public async Task ValidateAccessTokenAsync_ShouldFailForExpiredToken()
        {
            // Arrange
            var account = await SeedAccountAsync();
            
            var handler = new JwtSecurityTokenHandler();
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(_jwtConfig.Key)
            );
            var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, 
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
            );
            
            var now = DateTime.UtcNow;
            var expiredToken = new JwtSecurityToken(
                issuer: _jwtConfig.Issuer,
                audience: _jwtConfig.Audience,
                claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()) },
                notBefore: now.AddMinutes(-10),
                expires: now.AddMinutes(-5),
                signingCredentials: creds
            );
            
            var expiredTokenString = handler.WriteToken(expiredToken);

            // Act
            var principal = await _jwtService.ValidateAccessTokenAsync(expiredTokenString);

            // Assert
            Assert.Null(principal);
        }

        public void Dispose()
        {
            CleanupDatabase();
            _dbContext.Dispose();
        }
    }
}