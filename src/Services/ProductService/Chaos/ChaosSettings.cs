namespace ProductService.Chaos;

/// <summary>
/// "Chaos engineering" switch used to DEMONSTRATE the circuit breaker in OrderService.
/// When enabled, GET /api/products/{id} returns HTTP 500 (optionally after a delay).
///
/// Toggle it through the gateway:  POST /api/products/chaos  { "enabled": true, "delayMs": 0 }
///
/// Note: this is in-memory, so with several replicas (Kubernetes) each pod has its own switch.
/// </summary>
public sealed class ChaosSettings
{
    public bool Enabled { get; set; }
    public int DelayMs { get; set; }
}

public record ChaosRequest(bool Enabled, int DelayMs = 0);
