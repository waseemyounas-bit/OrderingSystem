using Microsoft.EntityFrameworkCore;
using ProductService.Domain;

namespace ProductService.Data;

/// <summary>Demo data with FIXED ids so the requests in requests.http work out of the box.</summary>
public static class ProductSeeder
{
    public static readonly Guid LaptopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid PhoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid HeadphonesId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid LimitedWatchId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedAsync(ProductDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        db.Products.AddRange(
            new Product { Id = LaptopId, Name = "Laptop Pro 14", Description = "Expensive - use it to trigger a payment failure (> 1000)", Price = 1499.00m, StockQuantity = 50 },
            new Product { Id = PhoneId, Name = "Smartphone X", Description = "Mid-range phone", Price = 699.00m, StockQuantity = 100 },
            new Product { Id = HeadphonesId, Name = "Wireless Headphones", Description = "Cheap - happy path", Price = 129.99m, StockQuantity = 200 },
            new Product { Id = LimitedWatchId, Name = "Limited Edition Watch", Description = "Only 2 in stock - use it to trigger an out-of-stock failure", Price = 249.00m, StockQuantity = 2 });

        await db.SaveChangesAsync();
    }
}
