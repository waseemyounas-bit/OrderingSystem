using System.Text.Json;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

public interface IMessagePublisher
{
    /// <summary>Publishes a command/event. The routing key is the message's type name.</summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class;
}

/// <summary>
/// Publishes messages to the topic exchange.
///
/// Reliability features you can see below:
///   - Persistent = true          : message is written to disk (survives a broker restart; the queue must be durable too)
///   - Publisher confirms         : PublishAsync only completes after RabbitMQ has ACKNOWLEDGED it stored the message.
///                                  If the broker rejects it, an exception is thrown instead of silently losing data.
///   - CorrelationId property     : carries our correlation id to the consumer (see CorrelationContext)
/// </summary>
public sealed class RabbitMqPublisher(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IMessagePublisher, IAsyncDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options = options.Value;

    // A channel must not be used for concurrent publishes, so we serialize access to it.
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage).Name;          // e.g. "ReserveStockCommand" -> routing key
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = messageType,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = CorrelationContext.Current ?? Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { ["source-service"] = _options.QueueName }
        };

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: messageType,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelLock.Release();
        }

        // {@Message} = Serilog "destructuring": the whole object is stored as structured data in Seq.
        logger.LogInformation("--> PUBLISHED {MessageType} {@Message}", messageType, message);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true }) return _channel;

        var conn = await connection.GetConnectionAsync(cancellationToken);

        // Enable publisher confirms + tracking: BasicPublishAsync awaits the broker's ack.
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareExchangesAsync(_channel, _options, cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }
    }
}
