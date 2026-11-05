using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeSnapshot.Api.Contracts;
using TradeSnapshot.Api.Observability;
using TradeSnapshot.Infrastructure.Persistence;
using TradeSnapshot.Infrastructure.Snapshots;

namespace TradeSnapshot.Api.Controllers;

[ApiController]
[Route("api/v1/accounts/{accountId}")]
[Produces("application/json")]
public sealed class AccountsController(
    TradeSnapshotDbContext db,
    IPortfolioSnapshotReader snapshots,
    ILogger<AccountsController> logger) : ControllerBase
{
    /// <summary>
    /// Daily portfolio snapshot in USD: positions by instrument (quantity, average unit cost, price, market value,
    /// unrealised P/L) and account totals.
    /// </summary>
    /// <param name="accountId">Account code, e.g. ACC-001.</param>
    /// <param name="date">Valuation date (yyyy-MM-dd). Trades dated on/before it are included; latest price/FX on/before it is used.</param>
    /// <param name="knownAt">Optional knowledge cutoff (ISO 8601). Only trade versions with as_of &lt;= known_at count,
    /// to reproduce a report as it was at that moment. Omit for the latest corrected view.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("snapshot")]
    [ProducesResponseType<PortfolioSnapshotResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(
        string accountId,
        [FromQuery(Name = "date"), Required] DateOnly? date,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        CancellationToken ct)
    {
        var accountCode = accountId.Trim().ToUpperInvariant();
        if (!await db.Accounts.AnyAsync(a => a.AccountCode == accountCode, ct))
            return AccountNotFound(accountCode);

        var start = Stopwatch.GetTimestamp();
        var snapshot = await snapshots.GetAsync(accountCode, date!.Value, knownAt?.UtcDateTime, ct);
        var totals = snapshot.Totals;

        Log.SnapshotProduced(logger, accountCode, snapshot.ValuationDate, snapshot.KnownAtUtc, totals.PositionCount,
            totals.TotalValueUsd, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (totals.UnpricedPositionCount > 0)
            Log.SnapshotHasUnpricedPositions(logger, accountCode, snapshot.ValuationDate, totals.UnpricedPositionCount);

        return Ok(new PortfolioSnapshotResponse(
            snapshot.AccountCode,
            snapshot.ValuationDate,
            snapshot.KnownAtUtc,
            snapshot.BaseCurrency,
            snapshot.Positions.Select(p => new PositionResponse(
                p.Isin, p.Symbol, p.Currency, p.TradeCount, p.Quantity, p.UnitCostLocal, p.PriceLocal, p.PriceDate,
                new FxConversionResponse($"USD-{p.Currency}", p.UsdFxRate, p.FxRateDate),
                p.MarketValueLocal, p.MarketValueUsd, p.CostBasisUsd, p.UnrealizedPnlUsd, p.ValuationStatus)).ToList(),
            new SnapshotTotalsResponse(totals.PositionCount, totals.UnpricedPositionCount, totals.TotalMarketValueUsd,
                totals.TotalCostBasisUsd, totals.TotalUnrealizedPnlUsd, totals.CashUsd, totals.TotalValueUsd)));
    }

    /// <summary>Most recent inbound trade messages for the account and what was decided for each.</summary>
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<IngestionEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvents(string accountId, [FromQuery, Range(1, 500)] int take = 50, CancellationToken ct = default)
    {
        var accountCode = accountId.Trim().ToUpperInvariant();
        if (!await db.Accounts.AnyAsync(a => a.AccountCode == accountCode, ct))
            return AccountNotFound(accountCode);

        var events = await db.TradeIngestionLog.AsNoTracking()
            .Where(e => e.AccountCode == accountCode)
            .OrderByDescending(e => e.ReceivedAtUtc).ThenByDescending(e => e.Id)
            .Take(take)
            .Select(e => new IngestionEventResponse(e.ExternalRef, e.AsOfUtc, e.ReceivedAtUtc, e.Outcome, e.Reason, e.Mode, e.ShadowOutcome, e.Version, e.CorrelationId))
            .ToListAsync(ct);
        return Ok(events);
    }

    private ObjectResult AccountNotFound(string accountCode) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Account not found", detail: $"No account '{accountCode}'.");
}
