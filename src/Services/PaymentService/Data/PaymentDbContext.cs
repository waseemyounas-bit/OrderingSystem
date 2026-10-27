using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;

namespace PaymentService.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20); // readable in the DB
            entity.HasIndex(p => p.OrderId).IsUnique(); // DB-level guarantee: never charge an order twice
        });
    }
}
