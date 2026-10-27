using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Declares exchanges, queues and bindings. Declarations are IDEMPOTENT: declaring something
/// that already exists with the same settings is a no-op, so every service can safely
/// declare what it needs at startup (no manual setup in the RabbitMQ UI required).
///
///   EXCHANGE = the "post office". Publishers send messages to an exchange, never to a queue directly.
///   QUEUE    = a mailbox that stores messages until a consumer processes them.
///   BINDING  = a rule "exchange X sends messages with routing key K to queue Q".
///
/// We use a TOPIC exchange: messages are routed by routing key (= message type name).
/// </summary>
public static class RabbitMqTopology
{
    public static async Task DeclareExchangesAsync(IChannel channel, RabbitMqOptions options, CancellationToken ct)
    {
        // durable: true -> the exchange survives a RabbitMQ restart
        await channel.ExchangeDeclareAsync(
            exchange: options.ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: options.DeadLetterExchangeName, type: ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);
    }

    public static async Task DeclareServiceQueueAsync(
        IChannel channel, RabbitMqOptions options, IEnumerable<string> routingKeys, CancellationToken ct)
    {
        await DeclareExchangesAsync(channel, options, ct);

        var deadLetterQueue = $"{options.QueueName}.dlq";

        // 1) Dead-letter queue: where "poison" messages end up for a human to inspect.
        await channel.QueueDeclareAsync(
            queue: deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(deadLetterQueue, options.DeadLetterExchangeName, routingKey: deadLetterQueue,
            cancellationToken: ct);

        // 2) Main queue. The x-dead-letter-* arguments tell RabbitMQ:
        //    "when a message is rejected (nack, requeue=false), send it to the dead-letter exchange".
        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = deadLetterQueue
        };
        await channel.QueueDeclareAsync(
            queue: options.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: queueArguments, cancellationToken: ct);

        // 3) Subscribe: bind the queue to every message type this service handles.
        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(options.QueueName, options.ExchangeName, routingKey, cancellationToken: ct);
        }
    }
}
