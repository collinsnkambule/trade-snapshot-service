using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeSnapshot.Infrastructure.Persistence;
using TradeSnapshot.Infrastructure.Snapshots;

namespace TradeSnapshot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTradeSnapshotPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TradeSnapshotDbContext>(options => options
            .UseSqlServer(connectionString, sql =>
            {
                // Transient faults (failover, throttling) are retried by EF. Every write path is a single
                // SaveChanges, so it is safe to retry as a unit.
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            }));

        services.AddScoped<IPortfolioSnapshotReader, PortfolioSnapshotReader>();
        return services;
    }
}
