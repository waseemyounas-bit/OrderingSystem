using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Messaging;

/// <summary>
/// One-line registration used by each service's Program.cs:
///
///   builder.Services.AddRabbitMqMessaging(builder.Configuration)
///       .AddHandler&lt;ReserveStockCommand, ReserveStockHandler&gt;()
///       .AddHandler&lt;ReleaseStockCommand, ReleaseStockHandler&gt;();
/// </summary>
public static class MessagingExtensions
{
    public static MessagingBuilder AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        var registry = new MessageHandlerRegistry();
        services.AddSingleton(registry);
        services.AddHostedService<RabbitMqConsumerService>();

        // Readiness: a pod that can't reach RabbitMQ should not receive traffic.
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

        return new MessagingBuilder(services, registry);
    }
}

public sealed class MessagingBuilder(IServiceCollection services, MessageHandlerRegistry registry)
{
    public MessagingBuilder AddHandler<TMessage, THandler>()
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        registry.Register<TMessage>();
        return this;
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await connection.GetConnectionAsync(timeout.Token);
            return connection.IsConnected
                ? HealthCheckResult.Healthy("RabbitMQ connection is open")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable", ex);
        }
    }
}
