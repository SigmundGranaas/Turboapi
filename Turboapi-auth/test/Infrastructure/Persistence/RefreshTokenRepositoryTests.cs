using Turboapi.Domain.Aggregates;
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;
using Xunit;
using System.Reflection;

namespace Turboapi.Infrastructure.Tests.Persistence
{
    [Collection("PostgresContainerCollection")]
    public class RefreshTokenRepositoryTests
    {
        private readonly PostgresContainerFixture _fixture;
        private readonly RefreshTokenRepository _refreshTokenRepository;
        private readonly AuthDbContext _dbContext;

        public RefreshTokenRepositoryTests(PostgresContainerFixture fixture)
        {
            _fixture = fixture;
            _dbContext = _fixture.CreateContext();
            _refreshTokenRepository = new RefreshTokenRepository(_dbContext);

            // Clean up relevant tables before each test
            _dbContext.RefreshTokens.RemoveRange(_dbContext.RefreshTokens);
            _dbContext.Accounts.RemoveRange(_dbContext.Accounts);
            _dbContext.SaveChanges();
        }

        private async Task<Account> SeedAccountAsync(string email = "testuser@example.com")
        {
            var account = Account.Create(Guid.NewGuid(), email, new[] { "User" });
            _dbContext.Accounts.Add(account);
            await _dbContext.SaveChangesAsync();
            return account;
        }

        private RefreshToken CreateTestRefreshToken(Guid accountId, string token, DateTime? expiresAt = null, bool isRevoked = false)
        {
            var actualExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7);
            
            var refreshToken = RefreshToken.Create(
                Guid.NewGuid(),
                accountId,
                token,
                actualExpiresAt
            );
            
            if (isRevoked)
            {
                refreshToken.Revoke();
            }

            return refreshToken;
        }

        /// <summary>
        /// Creates an expired refresh token using reflection for testing purposes only
        /// </summary>
        private RefreshToken CreateExpiredRefreshToken(Guid accountId, string token)
        {
            // Create using private constructor
            var refreshToken = (RefreshToken)Activator.CreateInstance(typeof(RefreshToken), true)!;
            
            // Set properties using reflection
            SetPrivateProperty(refreshToken, nameof(RefreshToken.Id), Guid.NewGuid());
            SetPrivateProperty(refreshToken, nameof(RefreshToken.AccountId), accountId);
            SetPrivateProperty(refreshToken, nameof(RefreshToken.Token), token);
            SetPrivateProperty(refreshToken, nameof(RefreshToken.ExpiresAt), DateTime.UtcNow.AddDays(-1)); // Expired
            SetPrivateProperty(refreshToken, nameof(RefreshToken.CreatedAt), DateTime.UtcNow.AddDays(-2));
            SetPrivateProperty(refreshToken, nameof(RefreshToken.IsRevoked), false);
            
            return refreshToken;
        }

        private static void SetPrivateProperty(object obj, string propertyName, object value)
        {
            var backingField = obj.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            backingField?.SetValue(obj, value);
        }

        [Fact]
        public async Task AddAsync_ShouldPersistRefreshToken()
        {
            // Arrange
            var account = await SeedAccountAsync();
            var refreshToken = CreateTestRefreshToken(account.Id, "token-add");

            // Act
            await _refreshTokenRepository.AddAsync(refreshToken);
            await _dbContext.SaveChangesAsync();

            // Assert
            var persistedToken = await _dbContext.RefreshTokens.FindAsync(refreshToken.Id);
            Assert.NotNull(persistedToken);
            Assert.Equal(refreshToken.Token, persistedToken.Token);
            Assert.Equal(account.Id, persistedToken.AccountId);
        }

        [Fact]
        public async Task GetByTokenAsync_ShouldReturnToken_WhenExistsAndNotRevokedOrExpired()
        {
            // Arrange
            var account = await SeedAccountAsync();
            var tokenString = "token-getbytoken";
            var refreshToken = CreateTestRefreshToken(account.Id, tokenString);
            await _refreshTokenRepository.AddAsync(refreshToken);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Act
            var fetchedToken = await _refreshTokenRepository.GetByTokenAsync(tokenString);

            // Assert
            Assert.NotNull(fetchedToken);
            Assert.Equal(tokenString, fetchedToken.Token);
        }

        [Fact]
        public async Task GetByTokenAsync_ShouldReturnNull_WhenNotExists()
        {
            // Act
            var fetchedToken = await _refreshTokenRepository.GetByTokenAsync("nonexistent-token");

            // Assert
            Assert.Null(fetchedToken);
        }

        [Fact]
        public async Task UpdateAsync_ShouldSaveChangesToRefreshToken()
        {
            // Arrange
            var account = await SeedAccountAsync();
            var refreshToken = CreateTestRefreshToken(account.Id, "token-update");
            await _refreshTokenRepository.AddAsync(refreshToken);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            var tokenToUpdate = await _refreshTokenRepository.GetByTokenAsync("token-update");
            Assert.NotNull(tokenToUpdate);

            // Act
            tokenToUpdate.Revoke("Test revocation");
            await _refreshTokenRepository.UpdateAsync(tokenToUpdate);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Assert
            var updatedToken = await _dbContext.RefreshTokens.FindAsync(refreshToken.Id);
            Assert.NotNull(updatedToken);
            Assert.True(updatedToken.IsRevoked);
            Assert.Equal("Test revocation", updatedToken.RevokedReason);
            Assert.NotNull(updatedToken.RevokedAt);
        }

        [Fact]
        public async Task GetActiveTokensForAccountAsync_ShouldReturnOnlyActiveTokens()
        {
            // Arrange
            var account1 = await SeedAccountAsync("acc1@example.com");
            var account2 = await SeedAccountAsync("acc2@example.com");

            // Account 1 tokens
            var activeToken1 = CreateTestRefreshToken(account1.Id, "active1-acc1");
            
            var revokedToken = CreateTestRefreshToken(account1.Id, "revoked-acc1");
            revokedToken.Revoke(); // Revoked
            
            // Use reflection helper to create expired token
            var expiredToken = CreateExpiredRefreshToken(account1.Id, "expired-acc1");
            
            var activeToken2 = CreateTestRefreshToken(account1.Id, "active2-acc1");

            // Account 2 token (should not be returned)
            var activeTokenAcc2 = CreateTestRefreshToken(account2.Id, "active-acc2");

            // Add all tokens to context
            _dbContext.RefreshTokens.Add(activeToken1);
            _dbContext.RefreshTokens.Add(revokedToken);
            _dbContext.RefreshTokens.Add(expiredToken);
            _dbContext.RefreshTokens.Add(activeToken2);
            _dbContext.RefreshTokens.Add(activeTokenAcc2);
            
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Act
            var activeTokens = (await _refreshTokenRepository.GetActiveTokensForAccountAsync(account1.Id)).ToList();

            // Assert
            Assert.Equal(2, activeTokens.Count);
            Assert.Contains(activeTokens, rt => rt.Token == "active1-acc1");
            Assert.Contains(activeTokens, rt => rt.Token == "active2-acc1");
            Assert.DoesNotContain(activeTokens, rt => rt.Token == "revoked-acc1");
            Assert.DoesNotContain(activeTokens, rt => rt.Token == "expired-acc1");
            Assert.DoesNotContain(activeTokens, rt => rt.Token == "active-acc2");
        }

        [Fact]
        public async Task GetActiveTokensForAccountAsync_ShouldReturnEmpty_WhenNoActiveTokens()
        {
            // Arrange
            var account = await SeedAccountAsync();
            
            var revokedToken = CreateTestRefreshToken(account.Id, "revoked");
            revokedToken.Revoke();
            
            var expiredToken = CreateExpiredRefreshToken(account.Id, "expired");

            _dbContext.RefreshTokens.Add(revokedToken);
            _dbContext.RefreshTokens.Add(expiredToken);
            await _dbContext.SaveChangesAsync();

            // Act
            var activeTokens = await _refreshTokenRepository.GetActiveTokensForAccountAsync(account.Id);

            // Assert
            Assert.Empty(activeTokens);
        }
    }
}