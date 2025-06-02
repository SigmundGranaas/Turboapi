using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Exceptions;
using Xunit;

namespace Turboapi.Domain.Tests
{
    public class OAuthAuthMethodTests
    {
        private readonly Guid _authMethodId = Guid.NewGuid();
        private readonly Guid _accountId = Guid.NewGuid();
        private const string ValidProviderName = "Google";
        private const string ValidExternalUserId = "google-user-123";
        private const string SampleAccessToken = "sample-access-token";
        private const string SampleRefreshToken = "sample-refresh-token";

        [Fact]
        public void Creation_ShouldSucceed_WithRequiredParameters()
        {
            // Act
            var authMethod = new OAuthAuthMethod(_authMethodId, _accountId, ValidProviderName, ValidExternalUserId);

            // Assert
            Assert.NotNull(authMethod);
            Assert.Equal(_authMethodId, authMethod.Id);
            Assert.Equal(_accountId, authMethod.AccountId);
            Assert.Equal(ValidProviderName, authMethod.ProviderName);
            Assert.Equal(ValidExternalUserId, authMethod.ExternalUserId);
            Assert.True(authMethod.CreatedAt <= DateTime.UtcNow && authMethod.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
            Assert.Null(authMethod.LastUsedAt);
            Assert.Null(authMethod.AccessToken);
            Assert.Null(authMethod.OAuthRefreshToken);
            Assert.Null(authMethod.TokenExpiry);
        }

        [Fact]
        public void Creation_ShouldSucceed_WithAllParameters()
        {
            // Arrange
            var expiry = DateTime.UtcNow.AddHours(1);

            // Act
            var authMethod = new OAuthAuthMethod(_authMethodId, _accountId, ValidProviderName, ValidExternalUserId,
                                                 SampleAccessToken, SampleRefreshToken, expiry);

            // Assert
            Assert.Equal(SampleAccessToken, authMethod.AccessToken);
            Assert.Equal(SampleRefreshToken, authMethod.OAuthRefreshToken);
            Assert.Equal(expiry, authMethod.TokenExpiry);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new OAuthAuthMethod(Guid.Empty, _accountId, ValidProviderName, ValidExternalUserId));
            Assert.Equal("AuthenticationMethod ID cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyAccountId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new OAuthAuthMethod(_authMethodId, Guid.Empty, ValidProviderName, ValidExternalUserId));
            Assert.Equal("Account ID for AuthenticationMethod cannot be empty.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Creation_ShouldFail_WithInvalidProviderName(string invalidProviderName)
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new OAuthAuthMethod(_authMethodId, _accountId, invalidProviderName, ValidExternalUserId));
            Assert.Equal("Provider name for AuthenticationMethod cannot be empty.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Creation_ShouldFail_WithInvalidExternalUserId(string invalidExternalId)
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new OAuthAuthMethod(_authMethodId, _accountId, ValidProviderName, invalidExternalId));
            Assert.Equal("External User ID cannot be empty for OAuthAuthMethod.", ex.Message);
        }

        [Fact]
        public void UpdateTokens_ShouldUpdateTokenPropertiesAndLastUsed()
        {
            // Arrange
            var authMethod = new OAuthAuthMethod(_authMethodId, _accountId, ValidProviderName, ValidExternalUserId);
            var newAccessToken = "new-access-token";
            var newRefreshToken = "new-refresh-token";
            var newExpiry = DateTime.UtcNow.AddHours(2);

            // Act
            authMethod.UpdateTokens(newAccessToken, newRefreshToken, newExpiry);

            // Assert
            Assert.Equal(newAccessToken, authMethod.AccessToken);
            Assert.Equal(newRefreshToken, authMethod.OAuthRefreshToken);
            Assert.Equal(newExpiry, authMethod.TokenExpiry);
            Assert.NotNull(authMethod.LastUsedAt);
            Assert.True(authMethod.LastUsedAt.Value <= DateTime.UtcNow && authMethod.LastUsedAt.Value > DateTime.UtcNow.AddSeconds(-5));
        }
    }
}