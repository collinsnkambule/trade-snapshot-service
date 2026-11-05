using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradeSnapshot.Api.Contracts;
using TradeSnapshot.Api.Ingestion;
using TradeSnapshot.Api.Observability;
using TradeSnapshot.Domain.Entities;
using TradeSnapshot.Infrastructure.Persistence;

namespace TradeSnapshot.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ReferenceDataController(TradeSnapshotDbContext db, TimeProvider clock, ILogger<ReferenceDataController> logger) : ControllerBase
{
    [HttpGet("instruments")]
    [ProducesResponseType<IReadOnlyList<InstrumentResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<InstrumentResponse>> GetInstruments(CancellationToken ct) =>
        await db.Instruments.AsNoTracking().OrderBy(i => i.Symbol)
            .Select(i => new InstrumentResponse(i.Isin, i.Symbol, i.Name, i.Currency))
            .ToListAsync(ct);

    /// <summary>Insert or replace the close price for an instrument and date (idempotent).</summary>
    [HttpPut("prices")]
    [ProducesResponseType<PriceUpsertRequest>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpsertPrice([FromBody] PriceUpsertRequest request, CancellationToken ct)
    {
        var isin = request.Isin!.Trim().ToUpperInvariant();
        var currency = request.Currency!.Trim().ToUpperInvariant();
        var instrument = await db.Instruments.AsNoTracking().SingleOrDefaultAsync(i => i.Isin == isin, ct);
        if (instrument is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Instrument not found", detail: $"No instrument with ISIN '{isin}'.");
        if (instrument.Currency != currency)
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Currency mismatch",
                detail: $"{instrument.Symbol} is quoted in {instrument.Currency}; price was supplied in {currency}.");

        var date = request.Date!.Value;
        var price = await db.InstrumentPrices.FindAsync([instrument.Id, date], ct);
        if (price is null)
            db.InstrumentPrices.Add(price = new InstrumentPrice { InstrumentId = instrument.Id, PriceDate = date });
        price.ClosePrice = request.Price!.Value;
        price.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        Log.PriceUpserted(logger, isin, date, price.ClosePrice, currency);
        return Ok(request);
    }

    /// <summary>Insert or replace a USD-based FX rate for a date (idempotent). rate = units of quote currency per 1 USD.</summary>
    [HttpPut("fx-rates")]
    [ProducesResponseType<FxRateUpsertRequest>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertFxRate([FromBody] FxRateUpsertRequest request, CancellationToken ct)
    {
        var quote = request.Pair![4..];
        var date = request.Date!.Value;
        var rate = await db.FxRates.FindAsync(["USD", quote, date], ct);
        if (rate is null)
            db.FxRates.Add(rate = new FxRate { BaseCurrency = "USD", QuoteCurrency = quote, RateDate = date });
        rate.Rate = request.Rate!.Value;
        rate.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        Log.FxRateUpserted(logger, request.Pair, date, rate.Rate);
        return Ok(request);
    }
}

[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
public sealed class AdminController(IOptionsMonitor<CorrectionHandlingOptions> options, ICorrectionHandlingGate gate) : ControllerBase
{
    /// <summary>Effective rollout configuration for duplicate/correction handling (optionally for one account).</summary>
    [HttpGet("correction-handling")]
    public IActionResult GetCorrectionHandling([FromQuery] string? account)
    {
        var current = options.CurrentValue;
        return Ok(new
        {
            configured_mode = current.Mode,
            pilot_accounts = current.PilotAccounts,
            account,
            effective_mode = account is null ? (object?)null : gate.ResolveFor(account.Trim().ToUpperInvariant()),
        });
    }
}
