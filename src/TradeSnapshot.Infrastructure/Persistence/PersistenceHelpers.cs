using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradeSnapshot.Infrastructure.Persistence;

public static class DbExceptionClassifier
{
    // 2601: duplicate key row in object with unique index; 2627: violation of PRIMARY KEY / UNIQUE constraint.
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>A lost race with a concurrent writer for the same trade: safe to re-read and re-decide.</summary>
    public static bool IsLostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || IsUniqueViolation(ex);
}

internal static class SqlScripts
{
    public static string Load(string name)
    {
        using var stream = typeof(SqlScripts).Assembly.GetManifestResourceStream($"Sql.{name}")
            ?? throw new InvalidOperationException($"Embedded SQL script '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Lets `dotnet ef` create the context without booting the web host.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TradeSnapshotDbContext>
{
    public TradeSnapshotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TradeSnapshot")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=TradeSnapshot;Trusted_Connection=True;TrustServerCertificate=True";

        return new TradeSnapshotDbContext(new DbContextOptionsBuilder<TradeSnapshotDbContext>()
            .UseSqlServer(connectionString)
            .Options);
    }
}
