using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Saga;

namespace OrderService.Data;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderSagaState> Sagas => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
            entity.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.ToTable("order_sagas");
            entity.HasKey(s => s.OrderId);
            entity.Property(s => s.CurrentStep).HasConversion<string>().HasMaxLength(40);
            entity.HasMany(s => s.Log).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SagaLogEntry>(entity =>
        {
            entity.ToTable("order_saga_log");
            entity.Property(l => l.Step).HasConversion<string>().HasMaxLength(40);
        });
    }
}
