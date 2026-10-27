using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Owns the ONE TCP connection this service keeps to RabbitMQ (registered as a singleton).
///
/// RabbitMQ concepts:
///   CONNECTION = an expensive TCP connection. Create one per application and share it.
///   CHANNEL    = a cheap, lightweight "virtual connection" inside it. We use one channel
///                for publishing and one for consuming.
///
/// In Docker/Kubernetes, our service can start BEFORE RabbitMQ is ready, so we retry
/// the first connection instead of crashing. Once connected, the client library's
/// AutomaticRecovery reconnects by itself if the broker restarts.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    : IAsyncDisposable
{
    private const int MaxConnectAttempts = 15;

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection is { IsOpen: true };

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return _connection!;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return _connection!;

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                ClientProvidedName = _options.QueueName, // shows up in the RabbitMQ management UI
                AutomaticRecoveryEnabled = true,          // reconnect automatically after network blips
                TopologyRecoveryEnabled = true            // re-declare queues/consumers after reconnecting
            };

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(cancellationToken);
                    logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _options.Host, _options.Port);
                    return _connection;
                }
                catch (BrokerUnreachableException ex) when (attempt < MaxConnectAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                    logger.LogWarning("RabbitMQ not reachable yet (attempt {Attempt}/{Max}): {Error}. Retrying in {Delay}s",
                        attempt, MaxConnectAttempts, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
    }
}
