using System;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Exceptions;
using Xunit;

namespace Turboapi.Domain.Tests
{
    public class RefreshTokenTests
    {
        private readonly Guid _tokenId = Guid.NewGuid();
        private readonly Guid _accountId = Guid.NewGuid();
        private const string ValidTokenString = "a-valid-refresh-token-string";
        private readonly TimeSpan _dateTimeComparisonTolerance = TimeSpan.FromSeconds(2);

        [Fact]
        public void Creation_ShouldSucceed_WithValidParametersAndFutureExpiry()
        {
            var expiresAt = DateTime.UtcNow.AddDays(7);
            var refreshToken = RefreshToken.Create(_tokenId, _accountId, ValidTokenString, expiresAt);
            Assert.NotNull(refreshToken);
            Assert.Equal(_tokenId, refreshToken.Id);
            Assert.Equal(_accountId, refreshToken.AccountId);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyId()
        {
            var ex = Assert.Throws<DomainException>(() => RefreshToken.Create(Guid.Empty, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("RefreshToken ID cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyAccountId()
        {
            var ex = Assert.Throws<DomainException>(() => RefreshToken.Create(_tokenId, Guid.Empty, ValidTokenString, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("Account ID for RefreshToken cannot be empty.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Creation_ShouldFail_WithInvalidTokenString(string invalidToken)
        {
            var ex = Assert.Throws<DomainException>(() => RefreshToken.Create(_tokenId, _accountId, invalidToken, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("Token string for RefreshToken cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithPastOrCurrentExpiry()
        {
            Assert.Throws<DomainException>(() => RefreshToken.Create(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddSeconds(-1)));
            Assert.Throws<DomainException>(() => RefreshToken.Create(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow));
        }

        [Fact]
        public void Revoke_ShouldMarkTokenAsRevoked_AndSetRevokedAtAndReason()
        {
            var refreshToken = RefreshToken.Create(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(7));
            refreshToken.Revoke("User logged out");
            Assert.True(refreshToken.IsRevoked);
            Assert.NotNull(refreshToken.RevokedAt);
        }

        [Fact]
        public void Revoke_ShouldBeIdempotent()
        {
            // *** FIX: Use the correct Create overload that takes a tokenId ***
            var refreshToken = RefreshToken.Create(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(7));
            refreshToken.Revoke("Initial revocation");
            var firstRevokedAt = refreshToken.RevokedAt;
            var firstReason = refreshToken.RevokedReason;
            
            refreshToken.Revoke("Second attempt");
            
            Assert.True(refreshToken.IsRevoked);
            Assert.Equal(firstRevokedAt, refreshToken.RevokedAt);
            Assert.Equal(firstReason, refreshToken.RevokedReason);
        }

        [Fact]
        public void IsExpired_ShouldReturnFalse_ForFutureExpiry()
        {
            var refreshToken = RefreshToken.Create(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddMinutes(10));
            Assert.False(refreshToken.IsExpired);
        }

        [Fact]
        public void IsExpired_ShouldReturnTrue_ForPastExpiry()
        {
            // Use the special test-only Create overload that allows expired dates
            var refreshToken = RefreshToken.Create(_accountId, ValidTokenString, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-10));
            Assert.True(refreshToken.IsExpired);
        }
    }
}