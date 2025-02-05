using Microsoft.EntityFrameworkCore;

namespace Turbo_pg_data.db;

public enum OrderStatus
{
    pending,
    completed,
    cancelled
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string? Category { get; set; }
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

public class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options) : base(options) { }
    
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Customer> Customers { get; set; }

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
            entity.HasMany(e => e.Orders)
                  .WithOne(e => e.Product)
                  .HasForeignKey(e => e.ProductId);
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
            entity.HasMany(e => e.Orders)
                  .WithOne(e => e.Customer)
                  .HasForeignKey(e => e.CustomerId);
        });
    }
}

public class OrderSummaryView
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; }
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
}

public static class OrderSummaryExtensions
{
    public static IQueryable<OrderSummaryView> GetOrderSummary(this TestContext context) =>
        context.Orders
            .Join(context.Products,
                o => o.ProductId,
                p => p.Id,
                (o, p) => new { Order = o, Product = p })
            .GroupJoin(context.Customers,
                op => op.Order.CustomerId,
                c => c.Id,
                (op, c) => new { op.Order, op.Product, Customers = c })
            .SelectMany(
                x => x.Customers.DefaultIfEmpty(),
                (x, c) => new OrderSummaryView
                {
                    OrderId = x.Order.Id,
                    CustomerName = c.Name,
                    ProductName = x.Product.Name,
                    Quantity = x.Order.Quantity,
                    TotalPrice = x.Product.Price * x.Order.Quantity,
                    OrderDate = x.Order.OrderDate,
                    Status = x.Order.Status
                });
}