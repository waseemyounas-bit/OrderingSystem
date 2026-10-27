using Microsoft.EntityFrameworkCore;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Domain;
using OrderService.Saga;
using Polly.CircuitBreaker;

namespace OrderService.Endpoints;

public record CreateOrderItemRequest(Guid ProductId, int Quantity);
public record CreateOrderRequest(string CustomerEmail, List<CreateOrderItemRequest> Items);

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync);

        group.MapGet("/", async (OrderDbContext db) =>
            await db.Orders.AsNoTracking().Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync());

        group.MapGet("/{id:guid}", async (Guid id, OrderDbContext db) =>
            await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id) is { } order
                ? Results.Ok(order)
                : Results.NotFound());

        // See the saga's state machine and its step-by-step history for one order.
        group.MapGet("/{id:guid}/saga", async (Guid id, OrderDbContext db) =>
        {
            var saga = await db.Sagas.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == id);
            if (saga is null) return Results.NotFound();

            var log = await db.Set<SagaLogEntry>().AsNoTracking()
                .Where(l => l.OrderId == id).OrderBy(l => l.Timestamp)
                .Select(l => new { l.Timestamp, l.Step, l.Message })
                .ToListAsync();

            return Results.Ok(new { saga.OrderId, saga.CurrentStep, saga.StartedAt, saga.UpdatedAt, Log = log });
        });

        // See the circuit breaker's live state: Closed / Open / HalfOpen / Isolated.
        group.MapGet("/circuit-breaker", (CircuitBreakerStateProvider state) =>
            Results.Ok(new { ProductServiceCircuit = state.CircuitState.ToString() }));
    }

    /// <summary>
    /// 1. Validate input.
    /// 2. SYNCHRONOUS call to ProductService (through the circuit breaker) for trusted name + price.
    /// 3. Start the saga (async) and immediately return 202 ACCEPTED - the order isn't final yet!
    ///    The client polls GET /api/orders/{id} to see Confirmed / Cancelled.
    /// </summary>
    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        ProductCatalogClient productCatalog,
        OrderSagaOrchestrator saga,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) || request.Items is not { Count: > 0 } ||
            request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["order"] = ["CustomerEmail is required and at least one item with Quantity > 0 must be provided"]
            });
        }

        // Merge duplicate lines (same product twice) into one line.
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new CreateOrderItemRequest(g.Key, g.Sum(i => i.Quantity)))
            .ToList();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerEmail = request.CustomerEmail,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var line in lines)
        {
            ProductDto? product;
            try
            {
                product = await productCatalog.GetProductAsync(line.ProductId, ct);
            }
            catch (ProductServiceUnavailableException ex)
            {
                // Graceful degradation: a clear 503 instead of a hanging request or a 500 stack trace.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Dependency unavailable");
            }

            if (product is null)
                return Results.Problem($"Product {line.ProductId} does not exist", statusCode: StatusCodes.Status400BadRequest);

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = line.Quantity
            });
        }

        order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        await saga.StartAsync(order, ct);
        logger.LogInformation("Order {OrderId} accepted (total {Total}); saga started", order.Id, order.TotalAmount);

        return Results.Accepted($"/api/orders/{order.Id}", new
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            order.TotalAmount,
            Message = "Order accepted and is being processed asynchronously. Poll the Location URL (or /saga) for the outcome."
        });
    }
}
