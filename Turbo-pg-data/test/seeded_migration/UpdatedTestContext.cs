
using Microsoft.EntityFrameworkCore;
using Turbo_pg_data.db;

public class InventoryStatus
{
    public string Name { get; set; }
    public string Category { get; set; }
    public int InventoryCount { get; set; }
    public int ReorderPoint { get; set; }
    public bool NeedsReorder { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string? Category { get; set; }
    public int InventoryCount { get; set; }
    public int ReorderPoint { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; }
    public int Quantity { get; set; }
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class UpdatedTestContext : DbContext
{
    public UpdatedTestContext(DbContextOptions<UpdatedTestContext> options) : base(options) { }
    
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Customer> Customers { get; set; }
    
    public IQueryable<InventoryStatus> InventoryStatuses => 
        FromExpression(() => InventoryStatuses);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.InventoryCount).HasColumnName("inventory_count");
            entity.Property(e => e.ReorderPoint).HasColumnName("reorder_point");

            entity.HasMany(e => e.Orders)
                .WithOne(e => e.Product)
                .HasForeignKey(e => e.ProductId)
                .IsRequired();
        });

        modelBuilder.Entity<InventoryStatus>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("inventory_status");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.InventoryCount).HasColumnName("inventory_count");
            entity.Property(e => e.ReorderPoint).HasColumnName("reorder_point");
            entity.Property(e => e.NeedsReorder).HasColumnName("needs_reorder");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.OrderDate).HasColumnName("order_date");
            entity.Property(e => e.Status)
                .HasColumnName("status");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");

            entity.HasOne(e => e.Product)
                .WithMany(p => p.Orders)
                .HasForeignKey(e => e.ProductId)
                .IsRequired();

            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(e => e.CustomerId);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}