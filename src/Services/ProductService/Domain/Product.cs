namespace ProductService.Domain;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }

    /// <summary>
    /// OPTIMISTIC CONCURRENCY TOKEN - mapped to PostgreSQL's hidden system column "xmin",
    /// which changes every time the row is updated.
    ///
    /// EF Core adds "WHERE xmin = @originalValue" to every UPDATE. If another order changed the
    /// row in between, 0 rows are updated and EF throws DbUpdateConcurrencyException.
    /// The message consumer then retries with fresh data -> two orders can never both take
    /// the last item in stock (no overselling), and we never need table locks.
    /// </summary>
    public uint Version { get; set; }
}

/// <summary>
/// Remembers WHICH order reserved HOW MUCH of a product. Needed for:
///   - Idempotency  : a duplicate ReserveStockCommand for the same order is detected and ignored
///   - Compensation : ReleaseStockCommand knows exactly what to give back
/// </summary>
public class StockReservation
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public bool Released { get; set; }
    public DateTime CreatedAt { get; set; }
}
