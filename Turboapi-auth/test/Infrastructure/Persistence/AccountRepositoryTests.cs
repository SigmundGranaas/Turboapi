using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces; // For IPasswordHasher if used directly in Account creation logic for tests
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace Turboapi.Infrastructure.Tests.Persistence
{
    [Collection("PostgresContainerCollection")]
    public class AccountRepositoryTests
    {
        private readonly PostgresContainerFixture _fixture;
        private readonly AuthDbContext _dbContext; // For setup and direct verification
        private readonly AccountRepository _accountRepository;

        // Dummy password hasher for test setup if Account creation requires it.
        private class TestPasswordHasher : IPasswordHasher
        {
            public string HashPassword(string password) => $"hashed_{password}";
            public bool VerifyPassword(string password, string hash) => $"hashed_{password}" == hash;
        }
        private readonly TestPasswordHasher _passwordHasher = new TestPasswordHasher();


        public AccountRepositoryTests(PostgresContainerFixture fixture)
        {
            _fixture = fixture;
            _dbContext = _fixture.CreateContext(); // Create a new context for each test method or class
            _accountRepository = new AccountRepository(_dbContext);

            // Clean up database before each test to ensure isolation
            // This can be done here or in InitializeAsync of a test-specific base class
            // For simplicity, we'll rely on the fixture's EnsureDeleted/Created for now,
            // but finer-grained cleanup might be needed for complex scenarios.
            // We should re-create the context to ensure it's fresh if data is modified.
             _dbContext.Accounts.RemoveRange(_dbContext.Accounts);
             _dbContext.SaveChanges();
        }
        
        private Account CreateTestAccount(string email, string[]? roles = null)
        {
            roles = roles ?? new[] { "User" };
            return Account.Create(Guid.NewGuid(), email, roles);
        }

        [Fact]
        public async Task AddAsync_ShouldPersistAccountAndItsChildren()
        {
            // Arrange
            var account = CreateTestAccount("test.add@example.com", new[] { "Admin", "User" });
            account.AddPasswordAuthMethod("password123", _passwordHasher);
            account.AddOAuthAuthMethod("Google", "google-id-123");
            // Add a refresh token to the account (assuming Account entity has a method like AddRefreshToken)
            // For now, we'll create it separately and link via AccountId for test purposes
            var refreshToken = RefreshToken.Create(Guid.NewGuid(), account.Id, "test-refresh-token-string", DateTime.UtcNow.AddDays(7));
            // account.AddRefreshToken(refreshToken); // If Account aggregate manages this internally

            // Act
            await _accountRepository.AddAsync(account);
            // If RefreshToken is not added via Account aggregate, add it to context directly for this test.
            // This depends on how RefreshToken is managed. For now, let's assume Account is the aggregate root for RTs too.
            // If the RefreshToken is part of the Account's collection that EF tracks, it should be saved.
            // If it's managed by a separate RefreshTokenRepository, this test might need adjustment.
            // Let's assume Account has a method like: account.GenerateNewRefreshToken("token", expiry)
            // which internally adds to its _refreshTokens collection.
            // For this test, let's assume the RT is part of the Account aggregate:
            _dbContext.RefreshTokens.Add(refreshToken); // Manually add if not part of Account's persisted collections by AddAsync
                                                        // Or ensure account.RefreshTokens collection is populated before AddAsync
                                                        // and EF Core is configured to cascade save.

            await _dbContext.SaveChangesAsync(); // Mimic Unit of Work save

            // Assert
            var persistedAccount = await _dbContext.Accounts
                .Include(a => a.Roles)
                .Include(a => a.AuthenticationMethods)
                .Include(a => a.RefreshTokens) // Ensure RefreshTokens are included
                .FirstOrDefaultAsync(a => a.Id == account.Id);

            Assert.NotNull(persistedAccount);
            Assert.Equal(account.Email, persistedAccount.Email);
            Assert.Equal(2, persistedAccount.Roles.Count);
            Assert.Contains(persistedAccount.Roles, r => r.Name == "Admin");
            Assert.Contains(persistedAccount.Roles, r => r.Name == "User");
            Assert.Equal(2, persistedAccount.AuthenticationMethods.Count);
            Assert.Contains(persistedAccount.AuthenticationMethods, am => am is PasswordAuthMethod);
            Assert.Contains(persistedAccount.AuthenticationMethods, am => am is OAuthAuthMethod oam && oam.ProviderName == "Google");
            
            // Assert Refresh Token - this depends on how RTs are linked.
            // If RefreshTokens are truly part of the Account aggregate and saved via cascade:
            Assert.Single(persistedAccount.RefreshTokens); 
            Assert.Equal(refreshToken.Token, persistedAccount.RefreshTokens.First().Token);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnAccountWithDetails_WhenExists()
        {
            // Arrange
            var originalAccount = CreateTestAccount("test.getbyid@example.com");
            originalAccount.AddPasswordAuthMethod("secure", _passwordHasher);
            await _accountRepository.AddAsync(originalAccount);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear(); // Detach all tracked entities

            // Act
            var fetchedAccount = await _accountRepository.GetByIdAsync(originalAccount.Id);

            // Assert
            Assert.NotNull(fetchedAccount);
            Assert.Equal(originalAccount.Id, fetchedAccount.Id);
            Assert.Equal(originalAccount.Email, fetchedAccount.Email);
            Assert.Single(fetchedAccount.Roles);
            Assert.Equal("User", fetchedAccount.Roles.First().Name);
            Assert.Single(fetchedAccount.AuthenticationMethods);
            Assert.IsType<PasswordAuthMethod>(fetchedAccount.AuthenticationMethods.First());
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
        {
            // Act
            var fetchedAccount = await _accountRepository.GetByIdAsync(Guid.NewGuid());

            // Assert
            Assert.Null(fetchedAccount);
        }

        [Fact]
        public async Task GetByEmailAsync_ShouldReturnAccountWithDetails_WhenExists()
        {
            // Arrange
            var email = "test.getbyemail@example.com";
            var originalAccount = CreateTestAccount(email);
            await _accountRepository.AddAsync(originalAccount);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Act
            var fetchedAccount = await _accountRepository.GetByEmailAsync(email);

            // Assert
            Assert.NotNull(fetchedAccount);
            Assert.Equal(originalAccount.Id, fetchedAccount.Id);
            Assert.Equal(email, fetchedAccount.Email);
        }
        
        [Fact]
        public async Task GetByEmailAsync_ShouldBeCaseInsensitive()
        {
            // Arrange
            var email = "Test.Case@Example.Com";
            var originalAccount = CreateTestAccount(email.ToLowerInvariant()); // Stored as lowercase
            await _accountRepository.AddAsync(originalAccount);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Act
            var fetchedAccount = await _accountRepository.GetByEmailAsync(email.ToUpperInvariant()); // Query with different case

            // Assert
            Assert.NotNull(fetchedAccount);
            Assert.Equal(originalAccount.Id, fetchedAccount.Id);
        }


        [Fact]
        public async Task GetByEmailAsync_ShouldReturnNull_WhenNotExists()
        {
            // Act
            var fetchedAccount = await _accountRepository.GetByEmailAsync("nonexistent@example.com");

            // Assert
            Assert.Null(fetchedAccount);
        }

        [Fact]
        public async Task UpdateAsync_ShouldSaveChangesToAccountAndChildren()
        {
            // Arrange
            var account = CreateTestAccount("test.update@example.com", new[] { "Viewer" });
            // DON'T add any auth methods initially
            await _accountRepository.AddAsync(account);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Fetch the account again to update it
            var accountToUpdate = await _accountRepository.GetByIdAsync(account.Id);
            Assert.NotNull(accountToUpdate);

            // Modify
            accountToUpdate.UpdateLastLogin();
            accountToUpdate.AddRole("Editor"); // New role
    
            // Add the first password auth method
            accountToUpdate.AddPasswordAuthMethod("newPass", _passwordHasher);

            // Act
            await _accountRepository.UpdateAsync(accountToUpdate);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();

            // Assert
            var updatedAccount = await _dbContext.Accounts
                .Include(a => a.Roles)
                .Include(a => a.AuthenticationMethods)
                .FirstOrDefaultAsync(a => a.Id == account.Id);

            Assert.NotNull(updatedAccount);
            Assert.NotNull(updatedAccount.LastLoginAt);
            Assert.Equal(2, updatedAccount.Roles.Count); // Viewer, Editor
            Assert.Contains(updatedAccount.Roles, r => r.Name == "Editor");
            Assert.Contains(updatedAccount.Roles, r => r.Name == "Viewer");
            Assert.Single(updatedAccount.AuthenticationMethods); // One PasswordAuthMethod
            Assert.IsType<PasswordAuthMethod>(updatedAccount.AuthenticationMethods.First());
        }
    }
}