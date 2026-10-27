namespace PaymentService.Domain;

public enum PaymentStatus { Approved, Declined }

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }          // unique -> at most one payment per order (idempotency)
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Bound from the "Payment" config section. Lets you change the demo rule without code changes.</summary>
public sealed class PaymentOptions
{
    public decimal MaxApprovedAmount { get; set; } = 1000m;
    public int SimulatedProcessingDelayMs { get; set; } = 500;
}
