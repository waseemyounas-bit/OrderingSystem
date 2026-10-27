namespace OrderService.Domain;

public enum OrderStatus
{
    Pending,        // created, saga running
    StockReserved,  // step 1 done, waiting for payment
    Confirmed,      // saga completed successfully
    Cancelled       // saga failed (and was compensated if needed)
}

public class Order
{
    public Guid Id { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }

    // Copied from ProductService at order time (a "snapshot"). If the product price changes
    // later, this order keeps the price the customer actually agreed to.
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
