namespace Turboapi.Models;

public class UserRole
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public Account Account { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
    
    public static readonly IReadOnlyList<string> AllRoles = new[] 
    { 
        User, 
        Admin
    };
}