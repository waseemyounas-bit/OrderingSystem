using System.Text;
using System.Text.Json;
using BuildingBlocks.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

/// <summary>
/// A BackgroundService (runs for the whole lifetime of the app) that consumes this
/// service's queue and dispatches each message to its IMessageHandler&lt;T&gt;.
///
/// Message lifecycle:
///   1. RabbitMQ pushes a message (at most PrefetchCount un-acked messages at a time).
///   2. We find the handler by message type, deserialize JSON, run the handler in a NEW DI scope
///      (fresh DbContext per message, just like a fresh one per HTTP request).
///   3. Success  -> BasicAck  : RabbitMQ deletes the message.
///      Failure  -> retry up to MaxHandlerAttempts, then BasicNack(requeue: false)
///                  -> RabbitMQ moves it to "{queue}.dlq" (dead-letter queue).
///
/// autoAck is FALSE on purpose: if the service crashes mid-processing, the message is
/// NOT lost - RabbitMQ redelivers it to another instance (important with multiple K8s replicas).
/// </summary>
public sealed class RabbitMqConsumerService(
    RabbitMqConnection connection,
    MessageHandlerRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registry.RoutingKeys.Count == 0)
        {
            logger.LogInformation("No message handlers registered - consumer not started");
            return;
        }

        var conn = await connection.GetConnectionAsync(stoppingToken);
        _channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareServiceQueueAsync(_channel, _options, registry.RoutingKeys, stoppingToken);

        // Fair dispatch: don't push more than N unacknowledged messages to this instance.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageReceivedAsync(delivery, stoppingToken);

        await _channel.BasicConsumeAsync(queue: _options.QueueName, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Consuming queue '{Queue}' for message types: {MessageTypes}",
            _options.QueueName, string.Join(", ", registry.RoutingKeys));
    }

    private async Task OnMessageReceivedAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var messageTypeName = delivery.BasicProperties.Type ?? delivery.RoutingKey;
        var correlationId = delivery.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString();

        // Continue the same correlation id as the publisher -> logs stay connected across services.
        using var _ = CorrelationContext.Begin(correlationId);

        if (!registry.TryGet(messageTypeName, out var registration))
        {
            logger.LogWarning("No handler for message type {MessageType} - dead-lettering it", messageTypeName);
            await _channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        var json = Encoding.UTF8.GetString(delivery.Body.Span);

        for (var attempt = 1; attempt <= _options.MaxHandlerAttempts; attempt++)
        {
            try
            {
                var message = JsonSerializer.Deserialize(json, registration.MessageType, RabbitMqPublisher.JsonOptions)
                              ?? throw new InvalidOperationException("Message body deserialized to null");

                logger.LogInformation("<-- RECEIVED {MessageType} (attempt {Attempt}) {@Message}",
                    messageTypeName, attempt, message);

                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    await registration.Dispatch(scope.ServiceProvider, message, stoppingToken);
                }

                await _channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                return;
            }
            catch (Exception ex) when (attempt < _options.MaxHandlerAttempts && !stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(500 * attempt);
                logger.LogWarning(ex, "Handler for {MessageType} failed (attempt {Attempt}/{Max}). Retrying in {Delay}ms",
                    messageTypeName, attempt, _options.MaxHandlerAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler for {MessageType} failed permanently. Moving message to dead-letter queue '{Dlq}'",
                    messageTypeName, $"{_options.QueueName}.dlq");
                await _channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is { IsOpen: true })
        {
            await _channel.CloseAsync(cancellationToken);
        }
    }
}
