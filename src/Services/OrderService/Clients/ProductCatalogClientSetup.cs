using BuildingBlocks.Observability;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;

namespace OrderService.Clients;

/// <summary>
/// ============================  CIRCUIT BREAKER  ============================
///
/// Every call to ProductService passes through this pipeline (outermost first):
///
///   request ─► [ RETRY ] ─► [ CIRCUIT BREAKER ] ─► [ TIMEOUT ] ─► HTTP ─► ProductService
///
///  TIMEOUT (innermost)  : one attempt may take at most 3s. A hanging call is turned into a failure.
///  CIRCUIT BREAKER      : watches the failure ratio of ATTEMPTS. If >= 50% of at least 5 attempts
///                         fail within 30s, it OPENS for 20s: calls are rejected instantly with
///                         BrokenCircuitException (no network traffic). That gives the sick
///                         service room to recover, and keeps OUR threads/connections free.
///                         After 20s it goes HALF-OPEN: one trial call decides CLOSED vs OPEN again.
///  RETRY (outermost)    : retries transient failures (5xx, 408, 429, network errors, timeouts)
///                         2 more times with exponential backoff + jitter. It does NOT retry
///                         when the circuit is open (BrokenCircuitException isn't "transient").
///
/// Why this order? Retry wraps the breaker, so every retry attempt is counted by the breaker,
/// and once the breaker opens, the remaining retries stop immediately.
///
/// What counts as failure? The HTTP defaults: HttpRequestException, TimeoutRejectedException,
/// and responses 5xx / 408 / 429. A 404 is a normal answer and does NOT count.
/// </summary>
public static class ProductCatalogClientSetup
{
    public static IServiceCollection AddProductCatalogClient(this IServiceCollection services, IConfiguration configuration)
    {
        // Lets us expose the live circuit state through an endpoint (GET /api/orders/circuit-breaker).
        var circuitStateProvider = new CircuitBreakerStateProvider();
        services.AddSingleton(circuitStateProvider);

        services.AddTransient<CorrelationIdDelegatingHandler>();

        services.AddHttpClient<ProductCatalogClient>(client =>
            {
                // Inside Docker/Kubernetes this is the SERVICE NAME, resolved by DNS: http://product-service:8080/
                client.BaseAddress = new Uri(configuration["Services:ProductServiceUrl"]
                                             ?? throw new InvalidOperationException("Services:ProductServiceUrl is not configured"));
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>() // forward X-Correlation-Id
            .AddResilienceHandler("product-service-pipeline", (pipeline, context) =>
            {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Resilience.ProductService");

                // 1) RETRY (outermost)
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential, // 200ms, 400ms, ...
                    UseJitter = true,                           // randomize to avoid "thundering herd"
                    OnRetry = args =>
                    {
                        logger.LogWarning("RETRY #{Attempt} calling ProductService in {Delay}ms (reason: {Reason})",
                            args.AttemptNumber + 1, (int)args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message ?? $"HTTP {(int?)args.Outcome.Result?.StatusCode}");
                        return default;
                    }
                });

                // 2) CIRCUIT BREAKER
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,                          // open when 50% of calls fail...
                    MinimumThroughput = 5,                       // ...out of at least 5 calls...
                    SamplingDuration = TimeSpan.FromSeconds(30), // ...within a 30s window
                    BreakDuration = TimeSpan.FromSeconds(20),    // stay open for 20s
                    StateProvider = circuitStateProvider,
                    OnOpened = args =>
                    {
                        logger.LogError("CIRCUIT OPENED - ProductService looks unhealthy. Failing fast for {Seconds}s",
                            args.BreakDuration.TotalSeconds);
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        logger.LogWarning("CIRCUIT HALF-OPEN - letting one trial request through to ProductService");
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("CIRCUIT CLOSED - ProductService is healthy again");
                        return default;
                    }
                });

                // 3) TIMEOUT per attempt (innermost)
                pipeline.AddTimeout(TimeSpan.FromSeconds(3));
            });

        return services;
    }
}
