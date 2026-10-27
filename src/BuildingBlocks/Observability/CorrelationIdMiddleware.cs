using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Observability;

/// <summary>
/// ASP.NET Core middleware: reads the incoming X-Correlation-Id header (or creates a new id)
/// and makes it the current correlation id for the whole HTTP request.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            // Write it back onto the request so a reverse proxy (YARP) forwards it downstream.
            context.Request.Headers[CorrelationContext.HeaderName] = correlationId;
        }

        // Echo it to the client so they can quote it in a bug report / search logs with it.
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        using (CorrelationContext.Begin(correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>
/// HttpClient handler: copies the current correlation id onto outgoing HTTP requests
/// (service-to-service calls), so the next service continues the same "trace".
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CorrelationContext.Current is { } correlationId &&
            !request.Headers.Contains(CorrelationContext.HeaderName))
        {
            request.Headers.Add(CorrelationContext.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public static class CorrelationIdExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
