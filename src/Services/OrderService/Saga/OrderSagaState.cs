namespace OrderService.Saga;

/// <summary>
/// The saga is a STATE MACHINE. Each value is "what we are waiting for now".
///
///   AwaitingStockReservation ──StockReserved──────► AwaitingPayment ──PaymentSucceeded──► Completed
///            │                                            │
///            └──StockReservationFailed──► Failed          └──PaymentFailed──► Compensating ──StockReleased──► Failed
/// </summary>
public enum SagaStep
{
    AwaitingStockReservation,
    AwaitingPayment,
    Compensating,
    Completed,
    Failed
}

/// <summary>
/// The saga's PERSISTED state. Stored in the database (not in memory!) so that if the
/// OrderService pod crashes or restarts in Kubernetes, the saga can continue where it
/// left off when the next event arrives - on any replica.
/// </summary>
public class OrderSagaState
{
    public Guid OrderId { get; set; }          // one saga instance per order
    public SagaStep CurrentStep { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SagaLogEntry> Log { get; set; } = [];
}

/// <summary>Human-readable audit trail of the saga - see GET /api/orders/{id}/saga.</summary>
public class SagaLogEntry
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public SagaStep Step { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
