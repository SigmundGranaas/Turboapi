using Microsoft.EntityFrameworkCore;

namespace Turboapi_geo.domain.query.model;

public class LocationReadContext : DbContext
{
    public DbSet<LocationReadEntity> Locations { get; set; }

    public LocationReadContext(DbContextOptions<LocationReadContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocationReadEntity>(entity =>
        {
            entity.ToTable("locations_read");
                
            // Fix: Correctly map the Id property to the 'id' column
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id");
                
            // Configure owner ID
            entity.Property(e => e.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();
                
            // Configure PostGIS geometry column
            entity.Property(e => e.Geometry)
                .HasColumnName("geometry")
                .HasColumnType("geometry(Point, 4326)")
                .IsRequired();

            // Configure soft delete
            entity.Property(e => e.IsDeleted)
                .HasColumnName("is_deleted")
                .HasDefaultValue(false);

            // Configure timestamps - making them explicitly not mapped
            entity.Ignore(e => e.CreatedAt);
            entity.Ignore(e => e.UpdatedAt);

            // Configure indexes
            entity.HasIndex(e => e.OwnerId)
                .HasDatabaseName("idx_locations_read_owner");
            
            entity.HasIndex(e => e.Geometry)
                .HasDatabaseName("idx_locations_read_geometry")
                .HasMethod("GIST");
            
            entity.HasIndex(e => e.IsDeleted)
                .HasDatabaseName("idx_locations_read_is_deleted");
        });
    }
}