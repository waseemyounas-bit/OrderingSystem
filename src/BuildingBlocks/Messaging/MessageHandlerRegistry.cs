using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Remembers which message types this service handles, and how to dispatch each one.
///   key   = message type name = RabbitMQ routing key (e.g. "StockReservedEvent")
///   value = CLR type to deserialize into + a delegate that calls the right IMessageHandler&lt;T&gt;
/// </summary>
public sealed class MessageHandlerRegistry
{
    public sealed record Registration(
        Type MessageType,
        Func<IServiceProvider, object, CancellationToken, Task> Dispatch);

    private readonly Dictionary<string, Registration> _registrations = new();

    public IReadOnlyCollection<string> RoutingKeys => _registrations.Keys;

    public void Register<TMessage>() where TMessage : class
    {
        _registrations[typeof(TMessage).Name] = new Registration(
            typeof(TMessage),
            (services, message, ct) => services
                .GetRequiredService<IMessageHandler<TMessage>>()
                .HandleAsync((TMessage)message, ct));
    }

    public bool TryGet(string messageTypeName, out Registration registration)
        => _registrations.TryGetValue(messageTypeName, out registration!);
}
