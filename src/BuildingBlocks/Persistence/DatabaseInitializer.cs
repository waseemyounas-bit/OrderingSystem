using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Waits for PostgreSQL to accept connections, creates the schema and runs optional seeding.
    ///
    /// Containers start in parallel, so Postgres may not be ready yet -> retry instead of crashing.
    ///
    /// NOTE: EnsureCreated() builds tables straight from the EF model. It's perfect for learning,
    /// but it cannot evolve an existing schema. Real projects use EF Core MIGRATIONS
    /// (dotnet ef migrations add ...), often applied by a Kubernetes Job / init container
    /// rather than by every replica at startup.
    /// </summary>
    public static async Task InitializeDatabaseAsync<TContext>(
        this WebApplication app, Func<TContext, Task>? seed = null) where TContext : DbContext
    {
        const int maxAttempts = 15;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();

                await db.Database.EnsureCreatedAsync();
                if (seed is not null) await seed(db);

                logger.LogInformation("Database for {DbContext} is ready", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Error}. Retrying in 3s",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
