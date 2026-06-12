using Brainy.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Brainy.Data;

/// <summary>
/// Registration helpers for the Brainy data access layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="BrainyDbContext"/> using the named connection string.
    /// </summary>
    public static IServiceCollection AddBrainyData(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection")
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' was not found.");

        services.AddDbContext<BrainyDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Register a scoped factory so individual DbContext instances can be
        // created on demand, independent of the scoped context used by Identity
        // stores.  A scoped lifetime avoids the lifetime conflict with the
        // scoped DbContextOptions registered by AddDbContext above.
        services.AddDbContextFactory<BrainyDbContext>(options =>
            options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

        // In Blazor Server, the DI scope lives for the entire circuit lifetime.
        // Multiple components can concurrently await DB operations, which would
        // cause EF Core's "second operation started" error if they all share one
        // scoped DbContext.  Registering IApplicationDbContext as transient gives
        // each injected service its own DbContext instance, eliminating that race.
        services.AddTransient<IApplicationDbContext>(
            sp => sp.GetRequiredService<IDbContextFactory<BrainyDbContext>>().CreateDbContext());

        return services;
    }
}
