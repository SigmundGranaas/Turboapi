using Turboapi.core;
using Xunit;

namespace Turboapi.test.unit;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _passwordHasher;

    public PasswordHasherTests()
    {
        _passwordHasher = new PasswordHasher();
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
}