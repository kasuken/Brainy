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

        return services;
    }
}
