using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Turboapi.Domain.Aggregates;

namespace Turboapi.Infrastructure.Persistence
{
    public class AuthDbContext : DbContext
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
        {
        }
        
        public DbSet<Account> Accounts { get; set; }
        public DbSet<AuthenticationMethod> AuthenticationMethods { get; set; }
        public DbSet<PasswordAuthMethod> PasswordAuthMethods { get; set; }
        public DbSet<OAuthAuthMethod> OAuthAuthMethods { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Role> Roles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply all IEntityTypeConfiguration classes from the current assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}