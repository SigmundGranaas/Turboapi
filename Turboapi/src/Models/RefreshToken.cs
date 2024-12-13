namespace Turboapi.Models;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiryTime { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? RevokedReason { get; set; }
    
    // Navigation property
    public Account Account { get; set; }
}