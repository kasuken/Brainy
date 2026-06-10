using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Brainy.Data;

/// <summary>
/// Applies any pending EF Core migrations at application startup.
/// Safe to call on every startup — it is a no-op when the schema is up to date.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task MigrateAsync(IServiceProvider services, bool seedDevelopmentData = false)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrainyDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BrainyDbContext>>();

        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
            {
                logger.LogInformation("Applying {Count} pending migration(s)…", pending.Count());
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogDebug("Database schema is up to date — no migrations needed.");
            }

            if (seedDevelopmentData)
            {
                await DevelopmentDataSeeder.SeedAsync(scope.ServiceProvider);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }
    }
}
