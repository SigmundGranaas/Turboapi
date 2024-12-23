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
        
        // Set all table names to lowercase
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("uuid")
                .ValueGeneratedOnAdd();
                
            
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email)
                .HasColumnName("email")
                .IsRequired();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.Property(e => e.LastLoginAt)
                .HasColumnName("last_login_at");
        });
        
        modelBuilder.Entity<AuthenticationMethod>(entity =>
        {
            entity.ToTable("authentication_methods");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("uuid")
                .ValueGeneratedOnAdd();
            
            entity.Property(e => e.Provider)
                .HasColumnName("provider")
                .IsRequired();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.Property(e => e.LastUsedAt)
                .HasColumnName("last_used_at");
                
            entity.Property(e => e.AccountId)
                .HasColumnName("account_id");
                
            // TPH configuration
            entity.HasDiscriminator<string>("auth_type")
                .HasValue<PasswordAuthentication>("Password")
                .HasValue<OAuthAuthentication>("OAuth")
                .HasValue<WebAuthnAuthentication>("WebAuthn");
        });
        
        modelBuilder.Entity<PasswordAuthentication>(entity =>
        {
            entity.Property(e => e.PasswordHash)
                .HasColumnName("password_hash");
                
            entity.Property(e => e.Salt)
                .HasColumnName("salt");
        });
        
        modelBuilder.Entity<OAuthAuthentication>(entity =>
        {
            entity.Property(e => e.ExternalUserId)
                .HasColumnName("external_user_id");
                
            entity.Property(e => e.AccessToken)
                .HasColumnName("access_token");
                
            entity.Property(e => e.RefreshToken)
                .HasColumnName("refresh_token");
                
            entity.Property(e => e.TokenExpiry)
                .HasColumnName("token_expiry");
        });
        
        modelBuilder.Entity<WebAuthnAuthentication>(entity =>
        {
            entity.Property(e => e.CredentialId)
                .HasColumnName("credential_id");
                
            entity.Property(e => e.PublicKey)
                .HasColumnName("public_key");
                
            entity.Property(e => e.DeviceName)
                .HasColumnName("device_name");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("uuid")
                .ValueGeneratedOnAdd();
            
            entity.Property(e => e.Role)
                .HasColumnName("role")
                .IsRequired();
            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.Property(e => e.AccountId)
                .HasColumnName("account_id");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("uuid")
                .ValueGeneratedOnAdd();
            
            entity.Property(e => e.Token)
                .IsRequired()
                .HasColumnName("token");

            
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
                
            entity.Property(e => e.AccountId)
                .HasColumnName("account_id");
                
            entity.Property(e => e.ExpiryTime)
                .HasColumnName("expiry_time");
                
            entity.Property(e => e.IsRevoked)
                .HasColumnName("is_revoked");
                
            entity.Property(e => e.RevokedReason)
                .HasColumnName("revoked_reason");
        });
    }
}