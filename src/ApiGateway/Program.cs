using System.Threading.RateLimiting;
using BuildingBlocks.Health;
using BuildingBlocks.Observability;

// ===============================  API GATEWAY  ===============================
//
//  Client ──► Gateway :5000 ──► /api/products/** ──► product-service:8080
//                          ├──► /api/orders/**   ──► order-service:8080
//                          └──► /api/payments/** ──► payment-service:8080
//
//  Why a gateway?
//   - ONE public entry point; internal services are not exposed to the internet
//   - Clients don't need to know how many services exist or where they run
//   - Cross-cutting concerns in one place: correlation ids, logging, rate limiting,
//     (and in real systems: authentication/JWT validation, CORS, caching ...)
//
//  Routes & destinations live in appsettings.json ("ReverseProxy" section), so they can be
//  changed per environment (Docker Compose / Kubernetes) via configuration only.
//
//  Kubernetes note: you may later replace or complement this with an Ingress controller
//  (NGINX/Traefik). An Ingress does edge routing; an app-level gateway like this one can
//  still own app-specific concerns (rate limits per client, request aggregation, auth).
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceLogging("ApiGateway");

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// RATE LIMITING: protect downstream services from being flooded.
// Policy "per-client": each client IP gets 30 requests per 10 seconds; extra requests get HTTP 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-client", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

builder.Services.AddServiceHealthChecks();

var app = builder.Build();

app.UseCorrelationId();          // generate X-Correlation-Id here, at the edge; YARP forwards it
app.UseServiceRequestLogging();
app.UseRateLimiter();

// ONE Swagger UI for the whole system: http://localhost:5000/swagger (pick a service in the top-right dropdown).
// The JSON documents are fetched THROUGH the gateway (see the *-docs-route entries in appsettings.json),
// and "Try it out" calls go through the gateway too - just like a real client.
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/docs/products/openapi/v1.json", "Product Service");
    options.SwaggerEndpoint("/docs/orders/openapi/v1.json", "Order Service");
    options.SwaggerEndpoint("/docs/payments/openapi/v1.json", "Payment Service");
    options.RoutePrefix = "swagger";
});

app.MapServiceHealthChecks();
app.MapGet("/", () => Results.Ok(new
{
    Service = "API Gateway",
    Swagger = "/swagger",
    Routes = new[] { "/api/products", "/api/orders", "/api/payments" }
}));

app.MapReverseProxy();

app.Run();
