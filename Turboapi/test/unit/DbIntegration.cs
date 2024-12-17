using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Npgsql;
using TurboApi.Data.Entity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Testcontainers.PostgreSql;
using System.Diagnostics;

namespace Turboapi.test.unit;

public class DbIntegration : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private string _connectionString;
    private readonly string _projectRoot;

    public DbIntegration()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithDatabase("turbo")
            .WithUsername("postgres")
            .WithPassword("your_password")
            .Build();
        
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "db")))
        {
            currentDir = currentDir.Parent;
        }
        
        if (currentDir == null)
            throw new Exception($"Could not find project root directory with 'db' folder. Current directory: {Directory.GetCurrentDirectory()}");
            
        _projectRoot = currentDir.FullName;
        Console.WriteLine($"Project root found at: {_projectRoot}");
    }
    
 public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        
        // Get the connection details from the container
        var host = _dbContainer.Hostname;
        var port = _dbContainer.GetMappedPublicPort(5432);
        
        // Build connection string for Flyway (using jdbc format)
        var flywayConnString = $"jdbc:postgresql://{host}:{port}/turbo?user=postgres&password=your_password";
        
        // Build connection string for EF Core
        _connectionString = $"Host={host};Port={port};Database=turbo;Username=postgres;Password=your_password";
        
        Console.WriteLine($"Using connection string: {_connectionString}");
        Console.WriteLine($"Using Flyway connection string: {flywayConnString}");
        
        await RunFlywayMigrations(flywayConnString);
    }

    private async Task RunFlywayMigrations(string flywayConnString)
    {
        var migrationsPath = Path.Combine(_projectRoot, "db", "migrations");
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "flyway",
                Arguments = $"migrate -url=\"{flywayConnString}\" -locations=filesystem:{migrationsPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _projectRoot
            }
        };

        Console.WriteLine($"Running Flyway with arguments: {process.StartInfo.Arguments}");
        
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"Flyway migration failed.\n" +
                $"Error: {error}\n" +
                $"Output: {output}\n" +
                $"Working Directory: {_projectRoot}\n" +
                $"Migrations Path: {migrationsPath}\n" +
                $"Connection String: {flywayConnString}"
            );
        }
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    private AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new AuthDbContext(options);
    }
    
    
    [Fact]
    public async Task ValidateFlywayMigrations_ShouldCreateAllTables()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var tableNames = await context.Database.SqlQuery<string>(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"
        ).ToListAsync();

        // Assert
        tableNames.Should().Contain("Accounts");
        tableNames.Should().Contain("AuthenticationMethods");
        tableNames.Should().Contain("UserRoles");
        tableNames.Should().Contain("RefreshTokens");
    }

    
    [Fact]
    public async Task ValidateFlywayMigrations_ShouldHaveCorrectColumns()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var columns = await context.Database.SqlQuery<ColumnInfo>(
            FormattableStringFactory.Create(@"
        SELECT 
            table_name,
            column_name,
            data_type,
            is_nullable = 'YES' as is_nullable
        FROM information_schema.columns 
        WHERE table_schema = 'public'
    ")).ToListAsync();

        // Assert
        // Verify Accounts table
        columns.Should().Contain(c => 
            c.table_name == "Accounts" && 
            c.column_name == "Email" && 
            c.data_type == "character varying" &&
            !c.is_nullable);

        // Verify AuthenticationMethods table
        columns.Should().Contain(c =>
            c.table_name == "AuthenticationMethods" &&
            c.column_name == "AuthType" &&
            c.data_type == "character varying" &&
            !c.is_nullable);
        
        // Verify UUID columns
        columns.Should().Contain(c =>
            c.table_name == "Accounts" &&
            c.column_name == "Id" &&
            c.data_type == "uuid" &&
            !c.is_nullable);
    }

    [Fact]
    public async Task ValidateFlywayMigrations_ShouldHaveCorrectConstraints()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var constraints = await context.Database.SqlQuery<ConstraintInfo>(
            FormattableStringFactory.Create(@"
            SELECT 
                tc.table_name,
                tc.constraint_type,
                kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
            WHERE tc.table_schema = 'public'
        ")).ToListAsync();

        // Assert
        // Check primary keys
        constraints.Should().Contain(c =>
            c.table_name == "Accounts" &&
            c.constraint_type == "PRIMARY KEY" &&
            c.column_name == "Id");

        // Check foreign keys
        constraints.Should().Contain(c =>
            c.table_name == "AuthenticationMethods" &&
            c.constraint_type == "FOREIGN KEY" &&
            c.column_name == "AccountId");
    }

    [Fact]
    public async Task ValidateFlywayMigrations_ShouldHaveCorrectIndices()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var indices = await context.Database.SqlQuery<string>(
            FormattableStringFactory.Create(@"
            SELECT indexname 
            FROM pg_indexes 
            WHERE schemaname = 'public'
        ")).ToListAsync();

        // Assert
        indices.Should().Contain("IX_Accounts_Email");
        indices.Should().Contain("IX_AuthenticationMethods_AccountId");
        indices.Should().Contain("IX_AuthenticationMethods_Provider_ExternalUserId");
        indices.Should().Contain("IX_UserRoles_AccountId_Role");
        indices.Should().Contain("IX_RefreshTokens_Token");
        indices.Should().Contain("IX_RefreshTokens_AccountId");
    }

    private record ColumnInfo(string table_name, string column_name, string data_type, bool is_nullable);
    private record ConstraintInfo(string table_name, string constraint_type, string column_name);
}