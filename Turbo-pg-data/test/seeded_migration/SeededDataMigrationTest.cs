using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turbo_pg_data.flyway;
using Xunit;

public class NewMigrationTest : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private IDatabaseSetupService _databaseSetupService;
    private string _baseMigrationsPath;
    private string _newMigrationsPath;

    public NewMigrationTest()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _baseMigrationsPath = FindRootPath.findMigrationPathFromSource("test", "base_migration");
        _newMigrationsPath =FindRootPath.findMigrationPathFromSource("test", "seeded_migration");
        
        // Run base migrations first
        _databaseSetupService = new DatabaseSetupService(_postgres.GetConnectionString(), _baseMigrationsPath);
        await _databaseSetupService.InitializeDatabaseAsync();
        await _databaseSetupService.RunMigrationsAsync();

        // Seed initial data
        var options = new DbContextOptionsBuilder<UpdatedTestContext>()
            .UseNpgsql(_databaseSetupService.GetConnectionString())
            .Options;
        using var context = new UpdatedTestContext(options);
        await DataSeeder.SeedInitialData(context);

        // Update service to use new migrations path
        _databaseSetupService = new DatabaseSetupService(_postgres.GetConnectionString(), _newMigrationsPath);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task NewMigration_ShouldAddInventoryColumns()
    {
        await _databaseSetupService.RunMigrationsAsync();

        var options = new DbContextOptionsBuilder<UpdatedTestContext>()
            .UseNpgsql(_databaseSetupService.GetConnectionString())
            .Options;

        using var context = new UpdatedTestContext(options);
        var product = await context.Products.FirstAsync();
        
        product.InventoryCount.Should().BeGreaterThan(0);
        product.ReorderPoint.Should().Be(10);
    }

    [Fact]
    public async Task NewMigration_ShouldPopulateInventoryCounts()
    {
        await _databaseSetupService.RunMigrationsAsync();

        using var conn = new NpgsqlConnection(_databaseSetupService.GetConnectionString());
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM products WHERE inventory_count > 0";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0);
    }
}

public static class DataSeeder
{
    public static async Task SeedInitialData(UpdatedTestContext context)
    {
        if (!context.Products.Any())
        {
            var products = new[]
            {
                new Product { Name = "Laptop", Price = 999.99m, Category = "Electronics" },
                new Product { Name = "Smartphone", Price = 599.99m, Category = "Electronics" },
                new Product { Name = "Python Book", Price = 49.99m, Category = "Books" }
            };
            context.Products.AddRange(products);
            await context.SaveChangesAsync();
        }
        
        if (!context.Customers.Any())
        {
            var customers = new[]
            {
                new Customer { Name = "John Doe", Email = "john@example.com", CreatedAt = DateTime.UtcNow },
                new Customer { Name = "Jane Smith", Email = "jane@example.com", CreatedAt = DateTime.UtcNow }
            };
            context.Customers.AddRange(customers);
            await context.SaveChangesAsync();
        }
    }
}