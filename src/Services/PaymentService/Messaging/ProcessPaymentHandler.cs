using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentService.Data;
using PaymentService.Domain;

namespace PaymentService.Messaging;

/// <summary>
/// SAGA STEP 2 (participant side): charge the customer.
/// Replies with PaymentSucceededEvent or PaymentFailedEvent.
/// </summary>
public sealed class ProcessPaymentHandler(
    PaymentDbContext db,
    IMessagePublisher publisher,
    IOptions<PaymentOptions> options,
    ILogger<ProcessPaymentHandler> logger) : IMessageHandler<ProcessPaymentCommand>
{
    public async Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken)
    {
        // IDEMPOTENCY: never charge the same order twice, even if the command is delivered twice.
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == command.OrderId, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Duplicate ProcessPaymentCommand for order {OrderId} - re-publishing previous result", command.OrderId);
            await PublishResultAsync(existing, cancellationToken);
            return;
        }

        // Pretend we're calling an external payment provider (Stripe, PayPal, ...).
        await Task.Delay(options.Value.SimulatedProcessingDelayMs, cancellationToken);

        var approved = command.Amount <= options.Value.MaxApprovedAmount;
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = command.OrderId,
            CustomerEmail = command.CustomerEmail,
            Amount = command.Amount,
            Status = approved ? PaymentStatus.Approved : PaymentStatus.Declined,
            DeclineReason = approved
                ? null
                : $"Card declined: amount {command.Amount} exceeds the demo limit of {options.Value.MaxApprovedAmount}",
            CreatedAt = DateTime.UtcNow
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Payment {Status} for order {OrderId}, amount {Amount}", payment.Status, command.OrderId, command.Amount);
        await PublishResultAsync(payment, cancellationToken);
    }

    private Task PublishResultAsync(Payment payment, CancellationToken ct) =>
        payment.Status == PaymentStatus.Approved
            ? publisher.PublishAsync(new PaymentSucceededEvent(payment.OrderId, payment.Id), ct)
            : publisher.PublishAsync(new PaymentFailedEvent(payment.OrderId, payment.DeclineReason!), ct);
}
