using Serilog.Context;

namespace BuildingBlocks.Observability;

/// <summary>
/// Holds the correlation id for the current logical operation.
///
/// WHY: A single user request travels Gateway -> OrderService -> RabbitMQ -> ProductService
/// -> RabbitMQ -> PaymentService ... Without a shared id it is impossible to connect those
/// log lines. We generate one id at the edge (gateway) and carry it everywhere:
///   - HTTP calls    : "X-Correlation-Id" header
///   - RabbitMQ      : the AMQP "correlation_id" message property
///   - Logs          : a "CorrelationId" property on every log event (filter by it in Seq!)
///
/// AsyncLocal = a value that flows with async/await, similar to a thread-static
/// but safe for async code. Each request/message gets its own value.
/// </summary>
public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogPropertyName = "CorrelationId";

    private static readonly AsyncLocal<string?> CurrentId = new();

    public static string? Current => CurrentId.Value;

    /// <summary>Sets the correlation id for everything executed inside the returned scope.</summary>
    public static IDisposable Begin(string correlationId)
    {
        var previous = CurrentId.Value;
        CurrentId.Value = correlationId;

        // Serilog: every log written inside this scope gets the CorrelationId property.
        var logScope = LogContext.PushProperty(LogPropertyName, correlationId);

        return new Scope(() =>
        {
            logScope.Dispose();
            CurrentId.Value = previous;
        });
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
