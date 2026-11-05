using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TradeSnapshot.Infrastructure.Persistence;

namespace TradeSnapshot.IntegrationTests;

/// <summary>
/// Boots the real API in-process against a real, throwaway SQL Server database (no in-memory provider: the
/// stored procedure, unique indexes and rowversion behaviour are part of what is being tested).
///
/// Uses LocalDB by default. Set TRADESNAPSHOT_TEST_SQL to point at any other SQL Server (e.g. a container in CI);
/// a unique database name is always generated and dropped afterwards.
/// </summary>
public sealed class SqlServerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString;

    public SqlServerApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("TRADESNAPSHOT_TEST_SQL")
            ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
        _connectionString = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"TradeSnapshot_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TradeSnapshot", _connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("TradeIngestion:CorrectionHandling:Mode", "Enabled");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TradeSnapshotDbContext>().Database.EnsureDeletedAsync();
        }
        await DisposeAsync();
    }
}
