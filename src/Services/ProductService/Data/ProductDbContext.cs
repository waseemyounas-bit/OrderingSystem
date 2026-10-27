using Microsoft.EntityFrameworkCore;
using ProductService.Domain;

namespace ProductService.Data;

/// <summary>
/// DATABASE PER SERVICE: only ProductService talks to "productdb".
/// Other services that need product data must ask via HTTP or react to events -
/// they never query these tables directly. This keeps services independently deployable.
/// </summary>
public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Version).IsRowVersion(); // Npgsql maps this to the xmin column
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            entity.HasIndex(r => r.OrderId);
        });
    }
}
