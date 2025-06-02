using System;
using Turboapi.Domain.Constants;
using Turboapi.Domain.Exceptions;

namespace Turboapi.Domain.Aggregates
{
    public class PasswordAuthMethod : AuthenticationMethod
    {
        public string PasswordHash { get; private set; }

        // Private constructor for EF Core - NO validation, NO base constructor call with parameters
        private PasswordAuthMethod() 
        {
        }

        public PasswordAuthMethod(Guid id, Guid accountId, string passwordHash)
            : base(id, accountId, AuthProviderNames.Password)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
                throw new DomainException("Password hash cannot be empty for PasswordAuthMethod.");
            PasswordHash = passwordHash;
        }
    }
}