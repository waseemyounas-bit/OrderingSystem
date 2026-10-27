using Microsoft.EntityFrameworkCore;
using ProductService.Chaos;
using ProductService.Data;
using ProductService.Domain;

namespace ProductService.Endpoints;

public record ProductDto(Guid Id, string Name, string Description, decimal Price, int StockQuantity);
public record CreateProductRequest(string Name, string Description, decimal Price, int StockQuantity);

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (ProductDbContext db) =>
            await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => ToDto(p))
                .ToListAsync());

        // This is the endpoint OrderService calls synchronously (protected by its circuit breaker).
        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, ChaosSettings chaos, ILogger<Program> logger) =>
        {
            if (chaos.Enabled)
            {
                if (chaos.DelayMs > 0) await Task.Delay(chaos.DelayMs);
                logger.LogWarning("CHAOS MODE: simulating a failure for GET product {ProductId}", id);
                return Results.Problem("Simulated failure (chaos mode is ON)", statusCode: StatusCodes.Status500InternalServerError);
            }

            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            return product is null ? Results.NotFound() : Results.Ok(ToDto(product));
        });

        group.MapPost("/", async (CreateProductRequest request, ProductDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Price <= 0 || request.StockQuantity < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["product"] = ["Name is required, Price must be > 0 and StockQuantity >= 0"]
                });

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                Price = request.Price,
                StockQuantity = request.StockQuantity
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return Results.Created($"/api/products/{product.Id}", ToDto(product));
        });

        // ----- Chaos switch (for the circuit breaker demo) -----
        group.MapGet("/chaos", (ChaosSettings chaos) => Results.Ok(chaos));
        group.MapPost("/chaos", (ChaosRequest request, ChaosSettings chaos, ILogger<Program> logger) =>
        {
            chaos.Enabled = request.Enabled;
            chaos.DelayMs = request.DelayMs;
            logger.LogWarning("Chaos mode set to {Enabled} (delay {DelayMs}ms)", chaos.Enabled, chaos.DelayMs);
            return Results.Ok(chaos);
        });
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Name, p.Description, p.Price, p.StockQuantity);
}
