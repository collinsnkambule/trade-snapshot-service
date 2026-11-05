using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradeSnapshot.Infrastructure.Persistence;

namespace TradeSnapshot.Infrastructure.Snapshots;

public sealed record PositionRow(
    string Isin,
    string Symbol,
    string Currency,
    int TradeCount,
    decimal Quantity,
    decimal? UnitCostLocal,
    decimal? PriceLocal,
    DateOnly? PriceDate,
    decimal? UsdFxRate,
    DateOnly? FxRateDate,
    decimal? MarketValueLocal,
    decimal? MarketValueUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    string ValuationStatus);

public sealed record AccountTotalsRow(
    int PositionCount,
    int UnpricedPositionCount,
    decimal TotalMarketValueUsd,
    decimal TotalCostBasisUsd,
    decimal TotalUnrealizedPnlUsd,
    decimal CashUsd,
    decimal TotalValueUsd);

public sealed record PortfolioSnapshot(
    string AccountCode,
    DateOnly ValuationDate,
    DateTime? KnownAtUtc,
    string BaseCurrency,
    IReadOnlyList<PositionRow> Positions,
    AccountTotalsRow Totals);

public interface IPortfolioSnapshotReader
{
    Task<PortfolioSnapshot> GetAsync(string accountCode, DateOnly valuationDate, DateTime? knownAtUtc, CancellationToken ct);
}

/// <summary>
/// Thin ADO.NET adapter over dbo.usp_GetPortfolioSnapshot. The API and the Task 2 artifact therefore share a
/// single implementation of the valuation rules, and the integration test exercises the SQL directly.
/// Uses the DbContext's connection (no second connection string, same resiliency settings).
/// </summary>
public sealed class PortfolioSnapshotReader(TradeSnapshotDbContext db) : IPortfolioSnapshotReader
{
    public async Task<PortfolioSnapshot> GetAsync(string accountCode, DateOnly valuationDate, DateTime? knownAtUtc, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere) await connection.OpenAsync(token);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "dbo.usp_GetPortfolioSnapshot";
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.Add(new SqlParameter("@AccountCode", SqlDbType.VarChar, 32) { Value = accountCode });
                command.Parameters.Add(new SqlParameter("@ValuationDate", SqlDbType.Date) { Value = valuationDate.ToDateTime(TimeOnly.MinValue) });
                command.Parameters.Add(new SqlParameter("@KnownAtUtc", SqlDbType.DateTime2) { Precision = 7, Value = (object?)knownAtUtc ?? DBNull.Value });

                await using var reader = await command.ExecuteReaderAsync(token);

                var positions = new List<PositionRow>();
                while (await reader.ReadAsync(token))
                {
                    positions.Add(new PositionRow(
                        Isin: reader.GetString("Isin"),
                        Symbol: reader.GetString("Symbol"),
                        Currency: reader.GetString("Currency"),
                        TradeCount: reader.GetInt32("TradeCount"),
                        Quantity: reader.GetDecimal("Quantity"),
                        UnitCostLocal: NullableDecimal(reader, "UnitCostLocal"),
                        PriceLocal: NullableDecimal(reader, "PriceLocal"),
                        PriceDate: NullableDate(reader, "PriceDate"),
                        UsdFxRate: NullableDecimal(reader, "UsdFxRate"),
                        FxRateDate: NullableDate(reader, "FxRateDate"),
                        MarketValueLocal: NullableDecimal(reader, "MarketValueLocal"),
                        MarketValueUsd: NullableDecimal(reader, "MarketValueUsd"),
                        CostBasisUsd: NullableDecimal(reader, "CostBasisUsd"),
                        UnrealizedPnlUsd: NullableDecimal(reader, "UnrealizedPnlUsd"),
                        ValuationStatus: reader.GetString("ValuationStatus")));
                }

                if (!await reader.NextResultAsync(token) || !await reader.ReadAsync(token))
                    throw new InvalidOperationException("usp_GetPortfolioSnapshot did not return the account summary result set.");

                var totals = new AccountTotalsRow(
                    PositionCount: reader.GetInt32("PositionCount"),
                    UnpricedPositionCount: reader.GetInt32("UnpricedPositionCount"),
                    TotalMarketValueUsd: reader.GetDecimal("TotalMarketValueUsd"),
                    TotalCostBasisUsd: reader.GetDecimal("TotalCostBasisUsd"),
                    TotalUnrealizedPnlUsd: reader.GetDecimal("TotalUnrealizedPnlUsd"),
                    CashUsd: reader.GetDecimal("CashUsd"),
                    TotalValueUsd: reader.GetDecimal("TotalValueUsd"));

                return new PortfolioSnapshot(accountCode, valuationDate, knownAtUtc, "USD", positions, totals);
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }, ct);
    }

    private static decimal? NullableDecimal(DbDataReader r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? null : r.GetDecimal(i);
    }

    private static DateOnly? NullableDate(DbDataReader r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    }
}
