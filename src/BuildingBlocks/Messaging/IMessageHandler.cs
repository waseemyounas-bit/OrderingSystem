namespace BuildingBlocks.Messaging;

/// <summary>
/// Implement this in a service to react to a message, e.g.
///     public class ReserveStockHandler : IMessageHandler&lt;ReserveStockCommand&gt;
///
/// IMPORTANT - handlers must be IDEMPOTENT: RabbitMQ guarantees "at-least-once" delivery,
/// so the same message can arrive twice (e.g. the service crashed after doing the work
/// but before ACKing). Handling it twice must not, for example, reserve stock twice.
/// </summary>
public interface IMessageHandler<in TMessage> where TMessage : class
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken);
}
