using Microsoft.EntityFrameworkCore;
using Turboapi.Models;

namespace TurboApi.Data.Entity;


public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<AuthenticationMethod> AuthenticationMethods { get; set; }
    public DbSet<UserRole> UserRoles { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.HasMany(e => e.AuthenticationMethods)
                .WithOne(e => e.Account)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasMany(e => e.Roles)
                .WithOne(e => e.Account)
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<AuthenticationMethod>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.HasOne(e => e.Account)
                .WithMany(e => e.AuthenticationMethods)
                .HasForeignKey(e => e.AccountId);
                 
            // TPH (Table-per-Hierarchy) configuration
            entity.HasDiscriminator<string>("AuthType")
                .HasValue<PasswordAuthentication>("Password")
                .HasValue<OAuthAuthentication>("OAuth")
                .HasValue<WebAuthnAuthentication>("WebAuthn");
        });
        
        modelBuilder.Entity<OAuthAuthentication>()
            .HasIndex(e => new { e.Provider, e.ExternalUserId })
            .IsUnique()
            .HasFilter("\"AuthType\" = 'OAuth'");
            
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.HasIndex(e => new { e.AccountId, e.Role })
                .IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.Token)
                .IsUnique();
                
            entity.HasOne(e => e.Account)
                .WithMany()
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}