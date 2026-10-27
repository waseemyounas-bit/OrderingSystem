using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using ProductService.Data;

namespace ProductService.Messaging;

/// <summary>
/// SAGA COMPENSATION: payment failed after stock was reserved, so "undo" step 1.
///
/// In a saga there is no distributed transaction / rollback across databases. Instead, every
/// step that changes data has a COMPENSATING ACTION that semantically reverses it.
///   ReserveStock  <-->  ReleaseStock
/// </summary>
public sealed class ReleaseStockHandler(
    ProductDbContext db,
    IMessagePublisher publisher,
    ILogger<ReleaseStockHandler> logger) : IMessageHandler<ReleaseStockCommand>
{
    public async Task HandleAsync(ReleaseStockCommand command, CancellationToken cancellationToken)
    {
        var reservations = await db.StockReservations
            .Where(r => r.OrderId == command.OrderId && !r.Released)
            .ToListAsync(cancellationToken);

        // IDEMPOTENCY: nothing left to release -> it was already done (duplicate message).
        if (reservations.Count == 0)
        {
            logger.LogWarning("No active reservations for order {OrderId} - already released", command.OrderId);
            await publisher.PublishAsync(new StockReleasedEvent(command.OrderId), cancellationToken);
            return;
        }

        var productIds = reservations.Select(r => r.ProductId).ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var reservation in reservations)
        {
            products[reservation.ProductId].StockQuantity += reservation.Quantity; // give the stock back
            reservation.Released = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("COMPENSATED: stock released for order {OrderId}", command.OrderId);
        await publisher.PublishAsync(new StockReleasedEvent(command.OrderId), cancellationToken);
    }
}
