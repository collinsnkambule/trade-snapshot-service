using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Infrastructure.Persistence;

public class TradeSnapshotDbContext(DbContextOptions<TradeSnapshotDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<InstrumentPrice> InstrumentPrices => Set<InstrumentPrice>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeVersion> TradeVersions => Set<TradeVersion>();
    public DbSet<TradeIngestionLogEntry> TradeIngestionLog => Set<TradeIngestionLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradeSnapshotDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Internal precision: 6dp for quantities and prices (covers fractional shares and sub-cent prices).
        // Money is rounded to 2dp only at the edge (final SELECT / response), never before aggregation.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 6);
        // All timestamps are UTC. datetime2 has no offset, so mark values read back as DateTimeKind.Utc
        // (otherwise they serialise without the trailing 'Z').
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(7)").HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
