
namespace Turbo_pg_data.flyway;

public class FindRootPath
{
    public static string findMigrationPathFromSource(params string[] paths)
    {
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "src")))
        {
            currentDir = currentDir.Parent;
        }
        
        var combined = new List<string>() { currentDir.ToString() };
        foreach (var path in paths)
        {
            combined.Add(path);
        }
        
        return Path.Combine(combined.ToArray());
    }
}