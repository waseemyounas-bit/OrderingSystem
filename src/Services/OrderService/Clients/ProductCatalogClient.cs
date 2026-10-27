using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.Clients;

/// <summary>
/// OrderService's own view of a product. We intentionally do NOT share ProductService's DTO
/// class: each service owns its models, so ProductService can add fields without breaking us.
/// </summary>
public record ProductDto(Guid Id, string Name, decimal Price, int StockQuantity);

public sealed class ProductServiceUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// TYPED HTTP CLIENT for ProductService. The HttpClient injected here already has the
/// resilience pipeline (retry + circuit breaker + timeout) attached - see ProductCatalogClientSetup.
/// This class only has to translate failures into something meaningful for the API.
/// </summary>
public sealed class ProductCatalogClient(HttpClient httpClient, ILogger<ProductCatalogClient> logger)
{
    public async Task<ProductDto?> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/products/{productId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound) return null; // a valid answer, not a failure
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ProductDto>(cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            // The circuit is OPEN: Polly did not even try the network call. This is the "fail fast" -
            // the user gets an answer in ~1ms instead of waiting for timeouts on a dead service.
            logger.LogWarning("Circuit breaker is OPEN - skipping call to ProductService for product {ProductId}", productId);
            throw new ProductServiceUnavailableException(
                "Product catalog is temporarily unavailable (circuit breaker OPEN). Please retry in a few seconds.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException)
        {
            // All retries exhausted (or timed out). These failures are what eventually OPEN the circuit.
            logger.LogError("ProductService call failed after retries for product {ProductId}: {Error}", productId, ex.Message);
            throw new ProductServiceUnavailableException("Product catalog failed to respond. Please retry later.", ex);
        }
    }
}
