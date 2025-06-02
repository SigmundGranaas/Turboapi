using Turboapi.Domain.Interfaces; // Using the Domain interface
using Turboapi.Infrastructure.Auth;   // Using the Infrastructure implementation
using Xunit;

namespace Turboapi.Infrastructure.Tests.Auth // New namespace for Infrastructure tests
{
    public class PasswordHasherTests
    {
        private readonly IPasswordHasher _passwordHasher;

        public PasswordHasherTests()
        {
            _passwordHasher = new PasswordHasher(); // Instantiating the new PasswordHasher
        }

        [Fact]
        public void HashPassword_ShouldCreateValidHash()
        {
            // Arrange
            var password = "TestPassword123!";

            // Act
            var hash = _passwordHasher.HashPassword(password);

            // Assert
            Assert.NotNull(hash);
            Assert.Contains(".", hash); // Verify the hash contains the salt separator
            Assert.True(hash.Length > 32); // Ensure hash is of sufficient length
        }

        [Fact]
        public void HashPassword_ShouldThrowArgumentNullException_ForNullPassword()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>("password", () => _passwordHasher.HashPassword(null!));
        }

        [Fact]
        public void HashPassword_ShouldThrowArgumentNullException_ForEmptyPassword()
        {
             // Act & Assert
            Assert.Throws<ArgumentNullException>("password", () => _passwordHasher.HashPassword(string.Empty));
        }


        [Fact]
        public void VerifyPassword_ShouldReturnTrueForCorrectPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var hash = _passwordHasher.HashPassword(password);

            // Act
            var result = _passwordHasher.VerifyPassword(password, hash);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalseForIncorrectPassword()
        {
            // Arrange
            var password = "TestPassword123!";
            var hash = _passwordHasher.HashPassword(password);
            var wrongPassword = "WrongPassword123!";

            // Act
            var result = _passwordHasher.VerifyPassword(wrongPassword, hash);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForNullPassword()
        {
            // Arrange
            var hash = _passwordHasher.HashPassword("somepassword");

            // Act
            var result = _passwordHasher.VerifyPassword(null!, hash);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForEmptyPassword()
        {
            // Arrange
            var hash = _passwordHasher.HashPassword("somepassword");

            // Act
            var result = _passwordHasher.VerifyPassword(string.Empty, hash);

            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForNullHashedPassword()
        {
            // Act
            var result = _passwordHasher.VerifyPassword("somepassword", null!);
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForEmptyHashedPassword()
        {
            // Act
            var result = _passwordHasher.VerifyPassword("somepassword", string.Empty);
            // Assert
            Assert.False(result);
        }


        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForInvalidHashedPasswordFormat_NoSeparator()
        {
            // Arrange
            var password = "TestPassword123!";
            var invalidHash = "justAHashWithoutSaltPart"; // No "." separator

            // Act
            var result = _passwordHasher.VerifyPassword(password, invalidHash);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForInvalidHashedPasswordFormat_TooManySeparators()
        {
            // Arrange
            var password = "TestPassword123!";
            var invalidHash = "salt.part.one.two"; // Too many "." separators

            // Act
            var result = _passwordHasher.VerifyPassword(password, invalidHash);

            // Assert
            Assert.False(result);
        }


        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForInvalidBase64Salt()
        {
            // Arrange
            var password = "TestPassword123!";
            var invalidHash = "!!!NotBase64!!!.somehashpart"; // Invalid Base64 for salt

            // Act
            var result = _passwordHasher.VerifyPassword(password, invalidHash);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void HashPassword_ProducesDifferentHashes_ForSamePasswordDueToSalt()
        {
            // Arrange
            var password = "TestPassword123!";

            // Act
            var hash1 = _passwordHasher.HashPassword(password);
            var hash2 = _passwordHasher.HashPassword(password);

            // Assert
            Assert.NotEqual(hash1, hash2); // Hashes should be different due to different salts
            Assert.True(_passwordHasher.VerifyPassword(password, hash1)); // Both should still verify correctly
            Assert.True(_passwordHasher.VerifyPassword(password, hash2));
        }
    }
}