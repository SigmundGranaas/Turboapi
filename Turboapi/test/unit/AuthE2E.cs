using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TurboApi.Data.Entity;
using Turboapi.dto;
using Turboapi.Models;
using Xunit;
using Testcontainers.PostgreSql;

namespace Turboapi.Tests.E2E;

public class AuthenticationE2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthenticationE2ETests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("turbo")
            .WithUsername("postgres")
            .WithPassword("your_password")
            .Build();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
                services.Remove(descriptor);

                services.AddDbContext<AuthDbContext>(options =>
                    options.UseNpgsql(_postgres.GetConnectionString()));
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        
        // Ensure database is created
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CompleteAuthenticationFlow_Success()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var registerRequest = new RegisterRequest("test@example.com", "SecurePass123!", "SecurePass123!");
        // Step 1: Register new user
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);


        Assert.True(registerResponse.IsSuccessStatusCode);
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registerResult);
        Assert.True(registerResult.Success);
        Assert.NotNull(registerResult.AccessToken);
        Assert.NotNull(registerResult.RefreshToken);

        // Verify account was created
        var account = await context.Accounts
            .Include(a => a.AuthenticationMethods)
            .Include(a => a.Roles)
            .FirstAsync(a => a.Email == "test@example.com");

        Assert.NotNull(account);
        Assert.Single(account.Roles);
        Assert.Equal("User", account.Roles[0].Role);
        Assert.Single(account.AuthenticationMethods);
        Assert.IsType<PasswordAuthentication>(account.AuthenticationMethods[0]);

        var loginRequest = new LoginRequest("test@example.com", "SecurePass123!");
        
        // Step 2: Login with credentials
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        Assert.True(loginResponse.IsSuccessStatusCode);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loginResult);
        Assert.True(loginResult.Success);
        Assert.NotNull(loginResult.AccessToken);
        Assert.NotNull(loginResult.RefreshToken);

        
        // Step 3: Refresh token
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        (
           loginResult.RefreshToken
        ));

        Assert.True(refreshResponse.IsSuccessStatusCode);
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshResult);
        Assert.True(refreshResult.Success);
        Assert.NotNull(refreshResult.AccessToken);
        Assert.NotNull(refreshResult.RefreshToken);
        Assert.NotEqual(loginResult.AccessToken, refreshResult.AccessToken);
        Assert.NotEqual(loginResult.RefreshToken, refreshResult.RefreshToken);
    }

    [Fact]
    public async Task DuplicateRegistration_Fails()
    {
        var registrationRequest = new RegisterRequest("duplicate@example.com", "SecurePass123!", "SecurePass123!");
        // First registration
        var firstResponse = await _client.PostAsJsonAsync("/api/auth/register", registrationRequest);

        Assert.True(firstResponse.IsSuccessStatusCode);

        // Second registration with same email
        var secondResponse = await _client.PostAsJsonAsync("/api/auth/register", registrationRequest);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, secondResponse.StatusCode);
        var result = await secondResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Email already registered", result.Error);
    }

    [Fact]
    public async Task InvalidLogin_Fails()
    {
        var registrationRequest = new RegisterRequest("invalid@example.com", "SecurePass123!", "SecurePass123!");

        // Register user first
        await _client.PostAsJsonAsync("/api/auth/register", registrationRequest);

        var loginRequest = new LoginRequest("invalid@example.com", "Wrong!");

        // Attempt login with wrong password
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        var result = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Invalid email or password", result.Error);
    }

    [Fact]
    public async Task InvalidRefreshToken_Fails()
    {
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        (
            "invalid-refresh-token"
        ));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        var result = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Invalid refresh token", result.Error);
    }

    [Fact]
    public async Task RevokedRefreshToken_CannotBeReused()
    {
        var registrationRequest = new RegisterRequest("revoke@example.com", "SecurePass123!", "SecurePass123!");

        // Register and login
        await _client.PostAsJsonAsync("/api/auth/register", registrationRequest);

        var loginRequest = new LoginRequest("revoke@example.com", "SecurePass123!");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.RefreshToken);

        // First refresh should succeed
        var firstRefreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(loginResult.RefreshToken));

        Assert.True(firstRefreshResponse.IsSuccessStatusCode);

        // Second refresh with same token should fail
        var secondRefreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(loginResult.RefreshToken));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, secondRefreshResponse.StatusCode);
        var result = await secondRefreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Token has been revoked", result.Error);
    }
}