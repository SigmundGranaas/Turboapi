using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turbo_pg_data.flyway;
using Xunit;

namespace Turbo_pg_data.test;

public class FlywayMigrationTests : IAsyncLifetime
{
    protected readonly PostgreSqlContainer _postgres;
    private IDatabaseSetupService _databaseSetupService;
    private string _migrationsPath;

    public FlywayMigrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    private async Task<int> GetMigrationCount()
    {
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM flyway_schema_history WHERE success = true";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task AssertTableExists(string tableName, bool shouldExist = true)
    {
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables 
                WHERE table_name = @tableName
            )";
        cmd.Parameters.AddWithValue("tableName", tableName);
        var exists = (bool)await cmd.ExecuteScalarAsync()!;
        exists.Should().Be(shouldExist);
    }

    private async Task AssertColumnExists(string tableName, string columnName, bool shouldExist = true)
    {
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EXISTS (
                SELECT FROM information_schema.columns 
                WHERE table_name = @tableName AND column_name = @columnName
            )";
        cmd.Parameters.AddWithValue("tableName", tableName);
        cmd.Parameters.AddWithValue("columnName", columnName);
        var exists = (bool)await cmd.ExecuteScalarAsync()!;
        exists.Should().Be(shouldExist);
    }
    
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _migrationsPath = FindRootPath.findMigrationPathFromSource("test", "base_migration");
        _databaseSetupService = new DatabaseSetupService(_postgres.GetConnectionString(), _migrationsPath);
        await _databaseSetupService.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task InitialMigration_ShouldCreateTables()
    {
        await _databaseSetupService.InitializeDatabaseAsync();

        await _databaseSetupService.RunMigrationsAsync();
        await AssertTableExists("products");
        await AssertTableExists("orders");
    }

    [Fact]
    public async Task ProductCategory_ShouldBeAdded()
    {
        await _databaseSetupService.RunMigrationsAsync();
        await AssertColumnExists("products", "category");
        
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM products WHERE category IS NOT NULL";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OrderStatus_ShouldCreateEnumAndColumn()
    {
        await _databaseSetupService.RunMigrationsAsync();
        await AssertColumnExists("orders", "status");
        
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'order_status')";
        var enumExists = (bool)await cmd.ExecuteScalarAsync()!;
        enumExists.Should().BeTrue();
    }

    [Fact]
    public async Task ViewCreation_ShouldCreateOrderSummary()
    {
        await _databaseSetupService.RunMigrationsAsync();
        
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_views WHERE viewname = 'order_summary')";
        var viewExists = (bool)await cmd.ExecuteScalarAsync()!;
        viewExists.Should().BeTrue();
    }

    [Fact]
    public async Task Constraints_ShouldBeApplied()
    {
        await _databaseSetupService.RunMigrationsAsync();
        
        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        
        // Test quantity constraint
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = @"
            INSERT INTO customers (name, email) VALUES ('Test Customer', 'test@example.com');
            INSERT INTO products (name, price, category) VALUES ('Test Product', 10.00, 'Test');
            INSERT INTO orders (product_id, customer_id, quantity) 
            VALUES ((SELECT id FROM products LIMIT 1), (SELECT id FROM customers LIMIT 1), -1)";
        
        await Assert.ThrowsAsync<PostgresException>(() => cmd1.ExecuteNonQueryAsync());

        // Test price constraint
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "INSERT INTO products (name, price) VALUES ('Test Product', -10.00)";
        await Assert.ThrowsAsync<PostgresException>(() => cmd2.ExecuteNonQueryAsync());
    }
}