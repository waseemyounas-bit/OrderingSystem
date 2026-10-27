using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace BuildingBlocks.Observability;

/// <summary>
/// Centralized logging setup (Serilog), identical for every service.
///
/// Sinks (= where logs go):
///   1. Console - what `docker logs` / `kubectl logs` show. In Kubernetes a log agent
///      (Fluent Bit, Promtail, ...) usually collects stdout from every pod.
///   2. Seq     - a log server with a web UI (http://localhost:8081) to search
///      STRUCTURED logs across all services, e.g.  CorrelationId = 'abc...'
///
/// "Structured" means properties are stored as fields, not just text:
///   logger.LogInformation("Order {OrderId} confirmed", orderId)
///   -> message text AND a searchable OrderId field.
/// </summary>
public static class LoggingExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {SourceContext}{NewLine}      {Message:lj}{NewLine}{Exception}";

    public static WebApplicationBuilder AddServiceLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341";

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)   // levels from appsettings "Serilog" section
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()                         // picks up CorrelationId pushed by CorrelationContext
            .Enrich.WithProperty("ServiceName", serviceName) // which microservice wrote the log
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .WriteTo.Seq(seqUrl));

        return builder;
    }

    /// <summary>
    /// One compact log line per HTTP request (method, path, status, duration).
    /// Health-check requests are logged at Verbose so Kubernetes probes don't flood the logs.
    /// </summary>
    public static IApplicationBuilder UseServiceRequestLogging(this IApplicationBuilder app)
        => app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
                : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
        });
}
