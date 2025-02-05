using Microsoft.EntityFrameworkCore;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.data;

public class ActivityContext : DbContext
{
    public DbSet<ActivityQueryDto> Activities { get; set; }

    public ActivityContext(DbContextOptions<ActivityContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityQueryDto>(entity =>
        {
            entity.ToTable("activity_query");
            entity.HasKey(e => e.Position);
            
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.ActivityId).HasColumnName("activity_id");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
        });
    }
}