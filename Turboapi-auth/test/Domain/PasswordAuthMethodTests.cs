using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Constants;
using Turboapi.Domain.Exceptions;
using Xunit;

namespace Turboapi.Domain.Tests
{
    public class PasswordAuthMethodTests
    {
        private readonly Guid _authMethodId = Guid.NewGuid();
        private readonly Guid _accountId = Guid.NewGuid();
        private const string ValidPasswordHash = "some_valid_password_hash";

        [Fact]
        public void Creation_ShouldSucceed_WithValidParameters()
        {
            // Act
            var authMethod = new PasswordAuthMethod(_authMethodId, _accountId, ValidPasswordHash);

            // Assert
            Assert.NotNull(authMethod);
            Assert.Equal(_authMethodId, authMethod.Id);
            Assert.Equal(_accountId, authMethod.AccountId);
            Assert.Equal(AuthProviderNames.Password, authMethod.ProviderName);
            Assert.Equal(ValidPasswordHash, authMethod.PasswordHash);
            Assert.True(authMethod.CreatedAt <= DateTime.UtcNow && authMethod.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
            Assert.Null(authMethod.LastUsedAt);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new PasswordAuthMethod(Guid.Empty, _accountId, ValidPasswordHash));
            Assert.Equal("AuthenticationMethod ID cannot be empty.", ex.Message);
        }

        [Fact]
        public void Creation_ShouldFail_WithEmptyAccountId()
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new PasswordAuthMethod(_authMethodId, Guid.Empty, ValidPasswordHash));
            Assert.Equal("Account ID for AuthenticationMethod cannot be empty.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void Creation_ShouldFail_WithInvalidPasswordHash(string invalidHash)
        {
            // Act & Assert
            var ex = Assert.Throws<DomainException>(() => new PasswordAuthMethod(_authMethodId, _accountId, invalidHash));
            Assert.Equal("Password hash cannot be empty for PasswordAuthMethod.", ex.Message);
        }

        [Fact]
        public void UpdateLastUsed_ShouldSetLastUsedAtToCurrentTime()
        {
            // Arrange
            var authMethod = new PasswordAuthMethod(_authMethodId, _accountId, ValidPasswordHash);
            var initialLastUsedAt = authMethod.LastUsedAt;

            // Act
            authMethod.UpdateLastUsed();

            // Assert
            Assert.Null(initialLastUsedAt); // Was null before
            Assert.NotNull(authMethod.LastUsedAt);
            Assert.True(authMethod.LastUsedAt.Value <= DateTime.UtcNow && authMethod.LastUsedAt.Value > DateTime.UtcNow.AddSeconds(-5));
        }
    }
}