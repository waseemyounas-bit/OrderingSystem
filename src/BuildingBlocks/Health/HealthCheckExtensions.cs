using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Health;

/// <summary>
/// Two health endpoints - exactly what Kubernetes probes expect:
///
///   /health/live  -> LIVENESS : "is the process alive?" Only checks itself.
///                    If it fails, Kubernetes RESTARTS the container.
///   /health/ready -> READINESS: "can I serve traffic right now?" Checks dependencies (DB, RabbitMQ).
///                    If it fails, Kubernetes stops sending traffic to the pod (no restart).
///
/// Never put dependency checks in liveness: a DB outage would make K8s restart every pod for nothing.
/// </summary>
public static class HealthCheckExtensions
{
    public const string LiveTag = "live";
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddServiceHealthChecks(this IServiceCollection services)
        => services.AddHealthChecks()
                   .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag]);

    public static IHealthChecksBuilder AddDatabaseHealthCheck<TContext>(this IHealthChecksBuilder builder)
        where TContext : DbContext
        => builder.AddCheck<DatabaseHealthCheck<TContext>>("database", tags: [ReadyTag]);

    public static WebApplication MapServiceHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains(LiveTag) });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
        return app;
    }
}

public sealed class DatabaseHealthCheck<TContext>(TContext db) : IHealthCheck where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database reachable")
            : HealthCheckResult.Unhealthy("Database unreachable");
}
