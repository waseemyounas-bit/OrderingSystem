using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Domain;

namespace OrderService.Saga;

/// <summary>
/// =============================  SAGA ORCHESTRATOR  =============================
///
/// THE WHOLE DISTRIBUTED TRANSACTION, READ TOP TO BOTTOM:
///
///   StartAsync                      -> save Order(Pending)  + send ReserveStockCommand
///   Handle(StockReservedEvent)      -> Order(StockReserved) + send ProcessPaymentCommand
///   Handle(StockReservationFailed)  -> Order(Cancelled)       (nothing to undo)
///   Handle(PaymentSucceededEvent)   -> Order(Confirmed)       SAGA COMPLETED
///   Handle(PaymentFailedEvent)      -> send ReleaseStockCommand  <-- COMPENSATION starts
///   Handle(StockReleasedEvent)      -> Order(Cancelled)       SAGA FAILED (but consistent!)
///
/// Every handler follows the same recipe:
///   1. load saga state from the DB
///   2. GUARD: is this event expected in the current step? (otherwise it's a duplicate -> ignore)
///   3. update order + saga state, save (one LOCAL transaction)
///   4. publish the next command
///
/// The orchestrator implements IMessageHandler for each reply event, so RabbitMQ messages
/// are routed straight here (see Program.cs).
///
/// KNOWN SIMPLIFICATION (good next step to learn): between "SaveChanges" and "Publish" the service
/// could crash, leaving the saga stuck. Production systems fix this "dual write" problem with the
/// TRANSACTIONAL OUTBOX pattern: store outgoing messages in the same DB transaction, and a background
/// worker publishes them afterwards.
/// </summary>
public sealed class OrderSagaOrchestrator(
    OrderDbContext db,
    IMessagePublisher publisher,
    ILogger<OrderSagaOrchestrator> logger) :
    IMessageHandler<StockReservedEvent>,
    IMessageHandler<StockReservationFailedEvent>,
    IMessageHandler<PaymentSucceededEvent>,
    IMessageHandler<PaymentFailedEvent>,
    IMessageHandler<StockReleasedEvent>
{
    // ------------------------------------------------------------------ START
    public async Task StartAsync(Order order, CancellationToken ct)
    {
        var saga = new OrderSagaState
        {
            OrderId = order.Id,
            CurrentStep = SagaStep.AwaitingStockReservation,
            StartedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        AddLog(saga, $"Saga started for order total {order.TotalAmount}. Asking ProductService to reserve stock.");

        db.Orders.Add(order);
        db.Sagas.Add(saga);
        await db.SaveChangesAsync(ct);

        await publisher.PublishAsync(new ReserveStockCommand(
            order.Id,
            order.Items.Select(i => new OrderItemMessage(i.ProductId, i.Quantity)).ToList()), ct);
    }

    // ------------------------------------------------------------------ STEP 1 OK
    public async Task HandleAsync(StockReservedEvent message, CancellationToken ct)
    {
        var (order, saga) = await LoadAsync(message.OrderId, ct);
        if (!IsExpected(saga, SagaStep.AwaitingStockReservation, nameof(StockReservedEvent))) return;

        order.Status = OrderStatus.StockReserved;
        MoveTo(saga, order, SagaStep.AwaitingPayment, "Stock reserved. Asking PaymentService to charge the customer.");
        await db.SaveChangesAsync(ct);

        await publisher.PublishAsync(new ProcessPaymentCommand(order.Id, order.CustomerEmail, order.TotalAmount), ct);
    }

    // ------------------------------------------------------------------ STEP 1 FAILED
    public async Task HandleAsync(StockReservationFailedEvent message, CancellationToken ct)
    {
        var (order, saga) = await LoadAsync(message.OrderId, ct);
        if (!IsExpected(saga, SagaStep.AwaitingStockReservation, nameof(StockReservationFailedEvent))) return;

        // Nothing was changed in other services yet -> no compensation needed.
        order.Status = OrderStatus.Cancelled;
        order.FailureReason = message.Reason;
        MoveTo(saga, order, SagaStep.Failed, $"Stock reservation failed: {message.Reason}. Nothing to compensate. Order cancelled.");
        await db.SaveChangesAsync(ct);

        logger.LogWarning("SAGA FAILED for order {OrderId}: {Reason}", order.Id, message.Reason);
    }

    // ------------------------------------------------------------------ STEP 2 OK
    public async Task HandleAsync(PaymentSucceededEvent message, CancellationToken ct)
    {
        var (order, saga) = await LoadAsync(message.OrderId, ct);
        if (!IsExpected(saga, SagaStep.AwaitingPayment, nameof(PaymentSucceededEvent))) return;

        order.Status = OrderStatus.Confirmed;
        MoveTo(saga, order, SagaStep.Completed, $"Payment {message.PaymentId} succeeded. Order CONFIRMED.");
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SAGA COMPLETED - order {OrderId} confirmed", order.Id);
    }

    // ------------------------------------------------------------------ STEP 2 FAILED -> COMPENSATE
    public async Task HandleAsync(PaymentFailedEvent message, CancellationToken ct)
    {
        var (order, saga) = await LoadAsync(message.OrderId, ct);
        if (!IsExpected(saga, SagaStep.AwaitingPayment, nameof(PaymentFailedEvent))) return;

        // Stock was already reserved in ProductService's database. We can't "roll back" another
        // service's committed transaction, so we ask it to run the COMPENSATING action.
        order.FailureReason = message.Reason;
        MoveTo(saga, order, SagaStep.Compensating, $"Payment failed: {message.Reason}. COMPENSATING: asking ProductService to release stock.");
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Payment failed for order {OrderId} - starting compensation", order.Id);
        await publisher.PublishAsync(new ReleaseStockCommand(order.Id), ct);
    }

    // ------------------------------------------------------------------ COMPENSATION DONE
    public async Task HandleAsync(StockReleasedEvent message, CancellationToken ct)
    {
        var (order, saga) = await LoadAsync(message.OrderId, ct);
        if (!IsExpected(saga, SagaStep.Compensating, nameof(StockReleasedEvent))) return;

        order.Status = OrderStatus.Cancelled;
        MoveTo(saga, order, SagaStep.Failed, "Stock released. Compensation complete. Order CANCELLED - system is consistent again.");
        await db.SaveChangesAsync(ct);

        logger.LogWarning("SAGA FAILED (compensated) for order {OrderId}", order.Id);
    }

    // ================================================================== helpers

    private async Task<(Order Order, OrderSagaState Saga)> LoadAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        var saga = await db.Sagas.FirstOrDefaultAsync(s => s.OrderId == orderId, ct);

        // Throwing makes the consumer retry, then dead-letter the message if it still fails.
        if (order is null || saga is null)
            throw new InvalidOperationException($"Order/saga {orderId} not found");

        return (order, saga);
    }

    /// <summary>
    /// IDEMPOTENCY GUARD. Messages can arrive twice (at-least-once delivery). If the saga is no
    /// longer in the step that expects this event, we've already processed it -> ignore safely.
    /// </summary>
    private bool IsExpected(OrderSagaState saga, SagaStep expected, string eventName)
    {
        if (saga.CurrentStep == expected) return true;

        logger.LogWarning("Ignoring {Event} for order {OrderId}: saga is in step {Current}, expected {Expected} (duplicate/late message)",
            eventName, saga.OrderId, saga.CurrentStep, expected);
        return false;
    }

    private void MoveTo(OrderSagaState saga, Order order, SagaStep next, string message)
    {
        logger.LogInformation("SAGA {OrderId}: {From} -> {To}", saga.OrderId, saga.CurrentStep, next);
        saga.CurrentStep = next;
        saga.UpdatedAt = order.UpdatedAt = DateTime.UtcNow;
        AddLog(saga, message);
    }

    private void AddLog(OrderSagaState saga, string message)
    {
        var entry = new SagaLogEntry
        {
            Id = Guid.NewGuid(),
            OrderId = saga.OrderId,
            Step = saga.CurrentStep,
            Message = message,
            Timestamp = DateTime.UtcNow
        };
        // Add explicitly so EF inserts it even though we didn't load the saga's Log collection.
        db.Add(entry);
    }
}
