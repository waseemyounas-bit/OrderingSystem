using Microsoft.EntityFrameworkCore;
using PaymentService.Data;

namespace PaymentService.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/payments").WithTags("Payments");

        group.MapGet("/", async (PaymentDbContext db) =>
            await db.Payments.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync());

        group.MapGet("/order/{orderId:guid}", async (Guid orderId, PaymentDbContext db) =>
            await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId) is { } payment
                ? Results.Ok(payment)
                : Results.NotFound());
    }
}
