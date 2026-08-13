using Brainy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Brainy.Web.Health;

/// <summary>
/// Verifies that the application can establish a connection to its SQL database.
/// </summary>
internal sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrainyDbContext>>();
        await using var database = await factory.CreateDbContextAsync(cancellationToken);

        return await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("SQL database is reachable.")
            : HealthCheckResult.Unhealthy("SQL database is not reachable.");
    }
}
