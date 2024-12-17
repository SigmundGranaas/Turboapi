namespace Turboapi.Models;

public class Account
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    
    // Navigation properties
    public List<UserRole> Roles { get; set; }
    public List<AuthenticationMethod> AuthenticationMethods { get; set; }
}