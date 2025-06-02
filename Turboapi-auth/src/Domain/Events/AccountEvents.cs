namespace Turboapi.Domain.Events
{
    public record AccountCreatedEvent(
        Guid AccountId,
        string Email,
        DateTime CreatedAt,
        IEnumerable<string> InitialRoles
    ) : IDomainEvent;

    public record AccountLastLoginUpdatedEvent(
        Guid AccountId,
        DateTime LastLoginAt
    ) : IDomainEvent;

    public record RoleAddedToAccountEvent(
        Guid AccountId,
        string RoleName,
        DateTime AddedAt
    ) : IDomainEvent;

    public record PasswordAuthMethodAddedEvent(
        Guid AccountId,
        Guid AuthMethodId,
        DateTime AddedAt
    ) : IDomainEvent;

    public record OAuthAuthMethodAddedEvent(
        Guid AccountId,
        Guid AuthMethodId,
        string ProviderName,
        string ExternalUserId,
        DateTime AddedAt
    ) : IDomainEvent;

  
    public record AccountLoggedInEvent(
        Guid AccountId,
        Guid AuthMethodId,    // The specific authentication method used
        string ProviderName,  // e.g., "Password", "Google"
        DateTime LoggedInAt
    ) : IDomainEvent;

    public record RefreshTokenGeneratedEvent(
        Guid AccountId,
        Guid RefreshTokenId,
        string TokenIdentifier,
        DateTime ExpiresAt,
        DateTime GeneratedAt
    ) : IDomainEvent;

    public record RefreshTokenRevokedEvent(
        Guid AccountId,
        Guid RefreshTokenId,
        string? RevocationReason,
        DateTime RevokedAt
    ) : IDomainEvent;
}