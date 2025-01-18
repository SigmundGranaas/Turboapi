using Turboapi_geo.infrastructure;

namespace Turboapi_geo.command;

public class DatabaseInitializationCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", true)
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole());
        var logger = loggerFactory.CreateLogger<DatabaseInitializer>();

        try
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var projectRoot = Directory.GetCurrentDirectory();
            var migrationsPath = Path.Combine(projectRoot, "db", "migrations");

            var dbInitializer = new DatabaseInitializer(
                connectionString!, 
                migrationsPath,
                logger);

            await dbInitializer.InitializeAsync();
            logger.LogInformation("Database initialization completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database");
            return 1;
        }
    }
}