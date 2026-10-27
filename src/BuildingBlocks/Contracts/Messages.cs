namespace BuildingBlocks.Contracts;

// =====================================================================================
//  MESSAGE CONTRACTS
//
//  These records are the "public API" between services over RabbitMQ.
//  The class name is used as the RabbitMQ routing key (e.g. "ReserveStockCommand"),
//  so renaming a record is a breaking change - treat them like a public HTTP API.
//
//  Two kinds of messages:
//    COMMAND  -> "please do X"        : one intended receiver, imperative name
//    EVENT    -> "X has happened"     : any number of listeners, past-tense name
//
//  Saga flow (orchestrated by OrderService):
//
//    OrderService ──ReserveStockCommand──► ProductService
//                 ◄──StockReservedEvent / StockReservationFailedEvent──
//    OrderService ──ProcessPaymentCommand──► PaymentService
//                 ◄──PaymentSucceededEvent / PaymentFailedEvent──
//    (on payment failure = COMPENSATION)
//    OrderService ──ReleaseStockCommand──► ProductService
//                 ◄──StockReleasedEvent──
// =====================================================================================

public record OrderItemMessage(Guid ProductId, int Quantity);

// ---------- Step 1: inventory ----------
public record ReserveStockCommand(Guid OrderId, List<OrderItemMessage> Items);
public record StockReservedEvent(Guid OrderId);
public record StockReservationFailedEvent(Guid OrderId, string Reason);

// ---------- Step 2: payment ----------
public record ProcessPaymentCommand(Guid OrderId, string CustomerEmail, decimal Amount);
public record PaymentSucceededEvent(Guid OrderId, Guid PaymentId);
public record PaymentFailedEvent(Guid OrderId, string Reason);

// ---------- Compensation (undo step 1 when step 2 fails) ----------
public record ReleaseStockCommand(Guid OrderId);
public record StockReleasedEvent(Guid OrderId);
