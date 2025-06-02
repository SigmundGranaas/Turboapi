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
            // Arrange
            var expiresAt = DateTime.UtcNow.AddDays(7);

            // Act
            var refreshToken = new RefreshToken(_tokenId, _accountId, ValidTokenString, expiresAt);

            // Assert
            Assert.NotNull(refreshToken);
            Assert.Equal(_tokenId, refreshToken.Id);
            Assert.Equal(_accountId, refreshToken.AccountId);
            Assert.Equal(ValidTokenString, refreshToken.Token);
            Assert.Equal(expiresAt, refreshToken.ExpiresAt);
            Assert.False(refreshToken.IsRevoked);
            Assert.Null(refreshToken.RevokedAt);
            Assert.Null(refreshToken.RevokedReason);
            Assert.True((DateTime.UtcNow - refreshToken.CreatedAt) < _dateTimeComparisonTolerance);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new RefreshToken(Guid.Empty, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("RefreshToken ID cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyAccountId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new RefreshToken(_tokenId, Guid.Empty, ValidTokenString, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("Account ID for RefreshToken cannot be empty.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Creation_ShouldFail_WithInvalidTokenString(string invalidToken)
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new RefreshToken(_tokenId, _accountId, invalidToken, DateTime.UtcNow.AddDays(1)));
            Assert.Equal("Token string for RefreshToken cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithPastOrCurrentExpiry()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddSeconds(-1)));
            Assert.Equal("RefreshToken expiration must be in the future.", ex.Message);

            var ex2 = Assert.Throws<DomainException>(() => new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow));
            Assert.Equal("RefreshToken expiration must be in the future.", ex2.Message);
        }

        [Fact]
        public void Revoke_ShouldMarkTokenAsRevoked_AndSetRevokedAtAndReason()
        {
            // Arrange
            var refreshToken = new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(7));
            const string reason = "User logged out";

            // Act
            refreshToken.Revoke(reason);

            // Assert
            Assert.True(refreshToken.IsRevoked);
            Assert.NotNull(refreshToken.RevokedAt);
            Assert.True((DateTime.UtcNow - refreshToken.RevokedAt.Value) < _dateTimeComparisonTolerance);
            Assert.Equal(reason, refreshToken.RevokedReason);
        }

        [Fact]
        public void Revoke_ShouldBeIdempotent()
        {
            // Arrange
            var refreshToken = new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddDays(7));
            refreshToken.Revoke("Initial revocation");
            var firstRevokedAt = refreshToken.RevokedAt;
            var firstReason = refreshToken.RevokedReason;

            // Act
            refreshToken.Revoke("Second attempt"); // Try to revoke again

            // Assert
            Assert.True(refreshToken.IsRevoked); // Still revoked
            Assert.Equal(firstRevokedAt, refreshToken.RevokedAt); // RevokedAt should not change
            Assert.Equal(firstReason, refreshToken.RevokedReason); // Reason should not change
        }

        [Fact]
        public void IsExpired_ShouldReturnFalse_ForFutureExpiry()
        {
            // Arrange
            var refreshToken = new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddMinutes(10));

            // Act & Assert
            Assert.False(refreshToken.IsExpired);
        }

        [Fact]
        public void IsExpired_ShouldReturnTrue_ForPastExpiry()
        {
            // Arrange
            // Create a token that is already expired by manipulating internal state for testing (not possible with current constructor)
            // So, we test by checking a token that will expire very soon.
            var refreshToken = new RefreshToken(_tokenId, _accountId, ValidTokenString, DateTime.UtcNow.AddMilliseconds(100));
            
            // Wait for it to expire
            System.Threading.Thread.Sleep(200);

            // Act & Assert
            Assert.True(refreshToken.IsExpired);
        }
    }
}