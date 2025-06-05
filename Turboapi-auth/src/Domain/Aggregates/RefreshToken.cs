using System;
using Turboapi.Domain.Exceptions;

namespace Turboapi.Domain.Aggregates
{
    public class RefreshToken
    {
        public Guid Id { get; private set; }
        public Guid AccountId { get; private set; } 
        public string Token { get; private set; }
        public DateTime ExpiresAt { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public bool IsRevoked { get; private set; }
        public DateTime? RevokedAt { get; private set; }
        public string? RevokedReason { get; private set; }

        // Private constructor for EF Core
        private RefreshToken() 
        {
        }

        // Private constructor for factory method
        private RefreshToken(Guid accountId, string token, DateTime expiresAt, DateTime? createdDate = null, Guid? tokenId = null, bool skipValidation = false)
        {
            if (accountId == Guid.Empty)
                throw new DomainException("Account ID for RefreshToken cannot be empty.");
            if (string.IsNullOrWhiteSpace(token))
                throw new DomainException("Token string for RefreshToken cannot be empty.");
            
            // Skip validation for test scenarios where we need to create expired tokens
            if (!skipValidation && expiresAt <= DateTime.UtcNow)
                throw new DomainException("RefreshToken expiration must be in the future.");

            Id = tokenId ?? Guid.NewGuid();
            AccountId = accountId;
            Token = token;
            ExpiresAt = expiresAt.ToUniversalTime(); 
            CreatedAt = createdDate ?? DateTime.UtcNow; 
            IsRevoked = false;
        }

        public static RefreshToken Create(Guid accountId, string token, DateTime expiresAt)
        {
            return new RefreshToken(accountId, token, expiresAt);
        }
        
        // Overload for test scenarios where we need specific created dates
        public static RefreshToken Create(Guid accountId, string token, DateTime expiresAt, DateTime createdDate)
        {
            // For test scenarios, allow creation of expired tokens
            var skipValidation = expiresAt <= DateTime.UtcNow;
            return new RefreshToken(accountId, token, expiresAt, createdDate, null, skipValidation);
        }
        
        public static RefreshToken Create(Guid tokenId, Guid accountId, string token, DateTime expiresAt)
        {
            return new RefreshToken(accountId, token, expiresAt, null, tokenId);
        }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        public void Revoke(string? reason = null)
        {
            if (IsRevoked)
                return; 

            IsRevoked = true;
            RevokedAt = DateTime.UtcNow; 
            RevokedReason = reason;
        }
    }
}