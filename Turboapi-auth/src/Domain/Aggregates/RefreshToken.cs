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

        private RefreshToken() 
        {
        }

        internal RefreshToken(Guid id, Guid accountId, string token, DateTime expiresAt)
        {
            if (id == Guid.Empty)
                throw new DomainException("RefreshToken ID cannot be empty.");
            if (accountId == Guid.Empty)
                throw new DomainException("Account ID for RefreshToken cannot be empty.");
            if (string.IsNullOrWhiteSpace(token))
                throw new DomainException("Token string for RefreshToken cannot be empty.");
            if (expiresAt <= DateTime.UtcNow)
                throw new DomainException("RefreshToken expiration must be in the future.");

            Id = id;
            AccountId = accountId;
            Token = token;
            ExpiresAt = expiresAt.ToUniversalTime(); // Ensure UTC
            CreatedAt = DateTime.UtcNow; // Ensure UTC
            IsRevoked = false;
        }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        public void Revoke(string? reason = null)
        {
            if (IsRevoked)
                return; 

            IsRevoked = true;
            RevokedAt = DateTime.UtcNow; // Ensure UTC
            RevokedReason = reason;
        }
    }
}