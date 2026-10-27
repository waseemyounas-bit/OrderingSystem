namespace BuildingBlocks.Messaging;

/// <summary>Bound from the "RabbitMq" configuration section (appsettings.json / environment variables).</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>The single TOPIC exchange every service publishes to.</summary>
    public string ExchangeName { get; set; } = "ordering.events";

    /// <summary>Messages that fail permanently are re-routed here (dead-lettered).</summary>
    public string DeadLetterExchangeName { get; set; } = "ordering.dead-letter";

    /// <summary>This service's own queue, e.g. "order-service". One queue per service.</summary>
    public string QueueName { get; set; } = string.Empty;

    /// <summary>How many times a handler is attempted before the message is dead-lettered.</summary>
    public int MaxHandlerAttempts { get; set; } = 3;

    /// <summary>Max unacknowledged messages RabbitMQ will push to this consumer at once.</summary>
    public ushort PrefetchCount { get; set; } = 10;
}
