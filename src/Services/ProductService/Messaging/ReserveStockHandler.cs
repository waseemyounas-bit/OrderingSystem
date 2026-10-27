using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using ProductService.Data;
using ProductService.Domain;

namespace ProductService.Messaging;

/// <summary>
/// SAGA STEP 1 (participant side): reserve stock for an order.
/// Replies with StockReservedEvent or StockReservationFailedEvent - the orchestrator
/// (OrderService) decides what happens next. This service knows nothing about payments.
/// </summary>
public sealed class ReserveStockHandler(
    ProductDbContext db,
    IMessagePublisher publisher,
    ILogger<ReserveStockHandler> logger) : IMessageHandler<ReserveStockCommand>
{
    public async Task HandleAsync(ReserveStockCommand command, CancellationToken cancellationToken)
    {
        // IDEMPOTENCY: this message may be a re-delivery. If we already reserved for this order,
        // don't reserve again - just re-send the result (the first reply may have been lost).
        if (await db.StockReservations.AnyAsync(r => r.OrderId == command.OrderId, cancellationToken))
        {
            logger.LogWarning("Duplicate ReserveStockCommand for order {OrderId} - already reserved, re-publishing result", command.OrderId);
            await publisher.PublishAsync(new StockReservedEvent(command.OrderId), cancellationToken);
            return;
        }

        var productIds = command.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Validate EVERYTHING first: all-or-nothing reservation.
        foreach (var item in command.Items)
        {
            string? failure = !products.TryGetValue(item.ProductId, out var product)
                ? $"Product {item.ProductId} does not exist"
                : product.StockQuantity < item.Quantity
                    ? $"Insufficient stock for '{product.Name}': requested {item.Quantity}, available {product.StockQuantity}"
                    : null;

            if (failure is not null)
            {
                logger.LogWarning("Stock reservation FAILED for order {OrderId}: {Reason}", command.OrderId, failure);
                await publisher.PublishAsync(new StockReservationFailedEvent(command.OrderId, failure), cancellationToken);
                return;
            }
        }

        foreach (var item in command.Items)
        {
            products[item.ProductId].StockQuantity -= item.Quantity;
            db.StockReservations.Add(new StockReservation
            {
                Id = Guid.NewGuid(),
                OrderId = command.OrderId,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                CreatedAt = DateTime.UtcNow
            });
        }

        // If another order modified one of these products meanwhile, the xmin check fails and EF
        // throws DbUpdateConcurrencyException. We let it bubble up: the consumer retries the
        // message with a FRESH DbContext (fresh stock numbers). See RabbitMqConsumerService.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Stock RESERVED for order {OrderId} ({ItemCount} item lines)", command.OrderId, command.Items.Count);
        await publisher.PublishAsync(new StockReservedEvent(command.OrderId), cancellationToken);
    }
}
