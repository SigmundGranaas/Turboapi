
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Exceptions;
using Turboapi.Domain.Interfaces; // For IPasswordHasher
using Turboapi.Domain.Constants; // For AuthProviderNames
using Xunit;

namespace Turboapi.Domain.Tests
{
    // Mock IPasswordHasher for testing purposes
    public class MockPasswordHasher : IPasswordHasher
    {
        public string ExpectedPassword { get; set; }
        public string HashedPasswordToReturn { get; set; } = "hashed_password_value";
        public bool WasHashPasswordCalled { get; private set; }

        public string HashPassword(string password)
        {
            ExpectedPassword = password;
            WasHashPasswordCalled = true;
            return HashedPasswordToReturn;
        }

        public bool VerifyPassword(string password, string hash)
        {
            throw new NotImplementedException("VerifyPassword is not needed for these specific tests.");
        }
    }

    public class AccountTests
    {
        private readonly Guid _accountId = Guid.NewGuid();
        private const string ValidEmail = "test@example.com";
        private readonly string[] _initialRoles = { "User", "Editor" };
        private readonly TimeSpan _dateTimeComparisonTolerance = TimeSpan.FromSeconds(2);
        private readonly MockPasswordHasher _mockPasswordHasher = new MockPasswordHasher();


        [Fact]
        public void Create_ShouldSucceed_WithValidEmailAndInitialRoles()
        {
            // Act
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Assert
            Assert.NotNull(account);
            Assert.Equal(_accountId, account.Id);
            Assert.Equal(ValidEmail.ToLowerInvariant(), account.Email);
            Assert.True((DateTime.UtcNow - account.CreatedAt) < _dateTimeComparisonTolerance, $"CreatedAt {account.CreatedAt} is too far from UtcNow {DateTime.UtcNow}");
            Assert.Null(account.LastLoginAt);
            Assert.Equal(_initialRoles.Length, account.Roles.Count);
            Assert.Equal(_initialRoles.OrderBy(r => r), account.Roles.Select(r => r.Name).OrderBy(r => r));
        }

        [Fact]
        public void Create_ShouldRaiseAccountCreatedEvent_WithCorrectDetails()
        {
            // Act
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Assert
            var createdEvents = account.DomainEvents.OfType<AccountCreatedEvent>().ToList();
            Assert.Single(createdEvents);
            var createdEvent = createdEvents.First();
            
            Assert.Equal(_accountId, createdEvent.AccountId);
            Assert.Equal(ValidEmail.ToLowerInvariant(), createdEvent.Email);
            Assert.Equal(account.CreatedAt, createdEvent.CreatedAt);
            Assert.Equal(_initialRoles.OrderBy(r => r), createdEvent.InitialRoles.OrderBy(r => r));
        }
        
        [Fact]
        public void Create_ShouldNotRaiseSeparateRoleAddedEvents_WhenAccountCreatedEventIsRaised()
        {
            // Act
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Assert
            Assert.DoesNotContain(account.DomainEvents, e => e is RoleAddedToAccountEvent);
        }


        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("invalidemail")]
        [InlineData("invalid@")]
        [InlineData("@example.com")]
        public void Create_ShouldFail_WithInvalidEmail(string invalidEmail)
        {
            // Act
            Action act = () => Account.Create(_accountId, invalidEmail, _initialRoles);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Invalid email format.", ex.Message);
        }
        
        [Fact]
        public void Create_ShouldFail_WithEmptyAccountId()
        {
            // Act
            Action act = () => Account.Create(Guid.Empty, ValidEmail, _initialRoles);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Account ID cannot be empty.", ex.Message);
        }

        [Fact]
        public void Create_ShouldFail_WithNullInitialRoles()
        {
            // Act
            Action act = () => Account.Create(_accountId, ValidEmail, null!);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Account must be created with at least one initial role.", ex.Message);
        }

        [Fact]
        public void Create_ShouldFail_WithEmptyInitialRolesArray()
        {
            // Act
            Action act = () => Account.Create(_accountId, ValidEmail, Array.Empty<string>());

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Account must be created with at least one initial role.", ex.Message);
        }
        
        [Fact]
        public void Create_ShouldFail_WithInitialRolesArrayContainingOnlyNullOrWhitespace()
        {
            // Act
            Action act = () => Account.Create(_accountId, ValidEmail, new string[] { null, " ", "" });

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Account must be created with at least one valid initial role name.", ex.Message);
        }
        
        // In test/Domain/AccountTests.cs, inside the AccountTests class

// Helper to create a RefreshToken for testing. Let's add it to the test class.
private RefreshToken CreateTestRefreshToken(Guid accountId, string tokenValue, bool isExpired = false)
{
    var expiresAt = isExpired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(7);
    // Using the test-friendly overload of the factory
    return RefreshToken.Create(accountId, tokenValue, expiresAt, DateTime.UtcNow.AddDays(isExpired ? -2 : -1));
}

[Fact]
public void RotateRefreshToken_WithValidToken_ShouldRevokeOldAndAddNewToken()
{
    // Arrange
    var account = Account.Create(_accountId, ValidEmail, _initialRoles);
    var oldTokenValue = "valid-old-token";
    var oldRefreshToken = CreateTestRefreshToken(_accountId, oldTokenValue);
    account.AddRefreshToken(oldRefreshToken); // Pre-load the token into the account
    account.ClearDomainEvents();

    var newRefreshTokenValue = "brand-new-refresh-token";
    var newRefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

    // Act
    var result = account.RotateRefreshToken(
        oldTokenValue,
        newRefreshTokenValue,
        newRefreshTokenExpiry
    );

    // Assert
    Assert.True(result.IsSuccess);
    var newDomainRefreshToken = result.Value;
    Assert.NotNull(newDomainRefreshToken);

    // 1. Verify the new token was created correctly and added to the account
    Assert.Equal(newRefreshTokenValue, newDomainRefreshToken.Token);
    Assert.Equal(_accountId, newDomainRefreshToken.AccountId);
    Assert.False(newDomainRefreshToken.IsRevoked);
    Assert.Contains(newDomainRefreshToken, account.RefreshTokens);

    // 2. Verify the old token was revoked
    var oldTokenInCollection = account.RefreshTokens.FirstOrDefault(rt => rt.Token == oldTokenValue);
    Assert.NotNull(oldTokenInCollection);
    Assert.True(oldTokenInCollection.IsRevoked);
    Assert.Equal("Rotated: New token pair issued", oldTokenInCollection.RevokedReason);

    // 3. Verify the correct domain events were raised
    var revokedEvents = account.DomainEvents.OfType<RefreshTokenRevokedEvent>().ToList();
    Assert.Single(revokedEvents);
    Assert.Equal(oldTokenInCollection.Id, revokedEvents.First().RefreshTokenId);
    Assert.Equal("Rotated: New token pair issued", revokedEvents.First().RevocationReason);

    var generatedEvents = account.DomainEvents.OfType<RefreshTokenGeneratedEvent>().ToList();
    Assert.Single(generatedEvents);
    Assert.Equal(newDomainRefreshToken.Id, generatedEvents.First().RefreshTokenId);

    // 4. Verify LastLogin/Activity was updated
    Assert.NotNull(account.LastLoginAt);
    Assert.Contains(account.DomainEvents, e => e is AccountLastLoginUpdatedEvent);
}


        [Fact]
        public void AddRole_ShouldAddRoleAndRaiseEvent_IfNotExists()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, new[] { "User" });
            account.ClearDomainEvents(); 
            const string newRoleName = "Admin";

            // Act
            account.AddRole(newRoleName);

            // Assert
            Assert.Equal(2, account.Roles.Count);
            Assert.Contains(account.Roles, r => r.Name == newRoleName);
            
            var addedEvents = account.DomainEvents.OfType<RoleAddedToAccountEvent>().ToList();
            Assert.Single(addedEvents);
            var addedEvent = addedEvents.First();

            Assert.Equal(_accountId, addedEvent.AccountId);
            Assert.Equal(newRoleName, addedEvent.RoleName);
            Assert.True((DateTime.UtcNow - addedEvent.AddedAt) < _dateTimeComparisonTolerance, $"AddedAt {addedEvent.AddedAt} is too far from UtcNow {DateTime.UtcNow}");
        }

        [Fact]
        public void AddRole_ShouldNotAddRoleAndNotRaiseEvent_IfExists()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            account.ClearDomainEvents(); 

            // Act
            account.AddRole(_initialRoles[0]); 

            // Assert
            Assert.Equal(_initialRoles.Length, account.Roles.Count); 
            Assert.Empty(account.DomainEvents);
        }

        [Fact]
        public void AddRole_ShouldFail_WithEmptyRoleName()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Act
            Action act = () => account.AddRole(" ");

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Role name cannot be empty.", ex.Message);
        }

        [Fact]
        public void UpdateLastLogin_ShouldSetLastLoginDateAndRaiseEvent()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            account.ClearDomainEvents(); 

            // Act
            account.UpdateLastLogin();

            // Assert
            Assert.NotNull(account.LastLoginAt);
            Assert.True((DateTime.UtcNow - account.LastLoginAt.Value) < _dateTimeComparisonTolerance, $"LastLoginAt {account.LastLoginAt.Value} is too far from UtcNow {DateTime.UtcNow}");
            
            var updatedEvents = account.DomainEvents.OfType<AccountLastLoginUpdatedEvent>().ToList();
            Assert.Single(updatedEvents);
            var updatedEvent = updatedEvents.First();

            Assert.Equal(_accountId, updatedEvent.AccountId);
            Assert.Equal(account.LastLoginAt.Value, updatedEvent.LastLoginAt);
        }
        
        [Fact]
        public void ClearDomainEvents_ShouldRemoveAllDomainEvents()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            Assert.NotEmpty(account.DomainEvents); 

            // Act
            account.ClearDomainEvents();

            // Assert
            Assert.Empty(account.DomainEvents);
        }

        [Fact]
        public void AddPasswordAuthMethod_ShouldCreateAndAssociatePasswordMethod_And_RaiseEvent()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            account.ClearDomainEvents();
            const string password = "securePassword123!";
            _mockPasswordHasher.HashedPasswordToReturn = "hashed_secure_password";

            // Act
            account.AddPasswordAuthMethod(password, _mockPasswordHasher);

            // Assert
            Assert.Single(account.AuthenticationMethods);
            var authMethod = account.AuthenticationMethods.First() as PasswordAuthMethod;
            Assert.NotNull(authMethod);
            Assert.Equal(_accountId, authMethod.AccountId);
            Assert.Equal(AuthProviderNames.Password, authMethod.ProviderName);
            Assert.Equal("hashed_secure_password", authMethod.PasswordHash);
            Assert.True(_mockPasswordHasher.WasHashPasswordCalled);
            Assert.Equal(password, _mockPasswordHasher.ExpectedPassword);

            var addedEvents = account.DomainEvents.OfType<PasswordAuthMethodAddedEvent>().ToList();
            Assert.Single(addedEvents);
            var addedEvent = addedEvents.First();
            Assert.Equal(_accountId, addedEvent.AccountId);
            Assert.Equal(authMethod.Id, addedEvent.AuthMethodId);
            Assert.True((DateTime.UtcNow - addedEvent.AddedAt) < _dateTimeComparisonTolerance);
        }

        [Fact]
        public void AddPasswordAuthMethod_ShouldFail_IfPasswordAuthAlreadyExists()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            account.AddPasswordAuthMethod("firstPassword", _mockPasswordHasher); // Add one
            
            // Act
            Action act = () => account.AddPasswordAuthMethod("secondPassword", _mockPasswordHasher);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Account already has a password authentication method.", ex.Message);
        }
        
        [Fact]
        public void AddPasswordAuthMethod_ShouldFail_IfPasswordIsEmpty()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Act
            Action act = () => account.AddPasswordAuthMethod("", _mockPasswordHasher);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal("Password cannot be empty.", ex.Message);
        }
        
        [Fact]
        public void AddPasswordAuthMethod_ShouldFail_IfPasswordHasherIsNull()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Act
            Action act = () => account.AddPasswordAuthMethod("password", null!);

            // Assert
            var ex = Assert.Throws<ArgumentNullException>(act);
            Assert.Equal("passwordHasher", ex.ParamName);
        }

        [Fact]
        public void AddOAuthAuthMethod_ShouldCreateAndAssociateOAuthMethod_And_RaiseEvent()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            account.ClearDomainEvents();
            const string providerName = "Google";
            const string externalUserId = "google-user-id-123";

            // Act
            account.AddOAuthAuthMethod(providerName, externalUserId);

            // Assert
            Assert.Single(account.AuthenticationMethods);
            var authMethod = account.AuthenticationMethods.First() as OAuthAuthMethod;
            Assert.NotNull(authMethod);
            Assert.Equal(_accountId, authMethod.AccountId);
            Assert.Equal(providerName, authMethod.ProviderName);
            Assert.Equal(externalUserId, authMethod.ExternalUserId);

            var addedEvents = account.DomainEvents.OfType<OAuthAuthMethodAddedEvent>().ToList();
            Assert.Single(addedEvents);
            var addedEvent = addedEvents.First();
            Assert.Equal(_accountId, addedEvent.AccountId);
            Assert.Equal(authMethod.Id, addedEvent.AuthMethodId);
            Assert.Equal(providerName, addedEvent.ProviderName);
            Assert.Equal(externalUserId, addedEvent.ExternalUserId);
            Assert.True((DateTime.UtcNow - addedEvent.AddedAt) < _dateTimeComparisonTolerance);
        }

        [Fact]
        public void AddOAuthAuthMethod_ShouldFail_IfOAuthMethodWithSameProviderAndExternalIdAlreadyExists()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            const string providerName = "Google";
            const string externalUserId = "google-user-id-123";
            account.AddOAuthAuthMethod(providerName, externalUserId); // Add one

            // Act
            Action act = () => account.AddOAuthAuthMethod(providerName, externalUserId);

            // Assert
            var ex = Assert.Throws<DomainException>(act);
            Assert.Equal($"OAuth method for provider '{providerName}' and external ID '{externalUserId}' already exists for this account.", ex.Message);
        }

        [Theory]
        [InlineData("", "ext-id")]
        [InlineData("Provider", "")]
        public void AddOAuthAuthMethod_ShouldFail_IfProviderOrExternalIdIsEmpty(string providerName, string externalId)
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);

            // Act & Assert
            if (string.IsNullOrWhiteSpace(providerName))
            {
                var ex = Assert.Throws<DomainException>(() => account.AddOAuthAuthMethod(providerName, externalId));
                Assert.Equal("OAuth Provider name cannot be empty.", ex.Message);
            }
            else if (string.IsNullOrWhiteSpace(externalId))
            {
                 var ex = Assert.Throws<DomainException>(() => account.AddOAuthAuthMethod(providerName, externalId));
                Assert.Equal("OAuth ExternalUser ID cannot be empty.", ex.Message);
            }
        }
        
        // Add this test method to the existing AccountTests class
        [Fact]
        public void RevokeRefreshToken_ShouldMarkTokenAsRevoked_AndRaiseEvent()
        {
            // Arrange
            var account = Account.Create(_accountId, ValidEmail, _initialRoles);
            var tokenToRevoke = RefreshToken.Create(_accountId, "token-to-revoke", DateTime.UtcNow.AddDays(7));
            var otherToken = RefreshToken.Create(_accountId, "other-token", DateTime.UtcNow.AddDays(7));
    
            // Use internal method for test setup
            account.AddRefreshToken(tokenToRevoke);
            account.AddRefreshToken(otherToken);
            account.ClearDomainEvents();

            // Act
            account.RevokeRefreshToken(tokenToRevoke.Token, "User logged out");

            // Assert
            var revokedToken = account.RefreshTokens.FirstOrDefault(rt => rt.Token == tokenToRevoke.Token);
            var untouchedToken = account.RefreshTokens.FirstOrDefault(rt => rt.Token == otherToken.Token);

            Assert.NotNull(revokedToken);
            Assert.True(revokedToken.IsRevoked);
            Assert.Equal("User logged out", revokedToken.RevokedReason);

            Assert.NotNull(untouchedToken);
            Assert.False(untouchedToken.IsRevoked);

            var revokedEvents = account.DomainEvents.OfType<RefreshTokenRevokedEvent>().ToList();
            Assert.Single(revokedEvents);
            Assert.Equal(revokedToken.Id, revokedEvents.First().RefreshTokenId);
            Assert.Equal("User logged out", revokedEvents.First().RevocationReason);
        }
    }
}