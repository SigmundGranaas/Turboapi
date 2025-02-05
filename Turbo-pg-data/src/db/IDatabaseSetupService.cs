namespace Turbo_pg_data.db;

public interface IDatabaseSetupService
{
    Task InitializeDatabaseAsync();
    Task RunMigrationsAsync();
    string GetConnectionString();
}