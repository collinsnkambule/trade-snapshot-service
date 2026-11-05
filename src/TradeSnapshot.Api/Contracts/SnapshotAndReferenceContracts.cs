using System.ComponentModel.DataAnnotations;

namespace TradeSnapshot.Api.Contracts;

public sealed record PortfolioSnapshotResponse(
    string AccountId,
    DateOnly ValuationDate,
    DateTime? KnownAtUtc,
    string BaseCurrency,
    IReadOnlyList<PositionResponse> Positions,
    SnapshotTotalsResponse Totals);

public sealed record PositionResponse(
    string Isin,
    string Symbol,
    string Currency,
    int TradeCount,
    decimal Quantity,
    decimal? UnitCost,
    decimal? Price,
    DateOnly? PriceDate,
    FxConversionResponse Fx,
    decimal? MarketValueLocal,
    decimal? MarketValueUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    string ValuationStatus);

/// <summary>How the local amount was converted: usd = local / rate, where rate = units of <c>Pair</c> quote per 1 USD.</summary>
public sealed record FxConversionResponse(string Pair, decimal? Rate, DateOnly? RateDate);

public sealed record SnapshotTotalsResponse(
    int PositionCount,
    int UnpricedPositionCount,
    decimal MarketValueUsd,
    decimal CostBasisUsd,
    decimal UnrealizedPnlUsd,
    decimal CashUsd,
    decimal TotalValueUsd);

public sealed class PriceUpsertRequest
{
    [Required, StringLength(12, MinimumLength = 12)]
    public string? Isin { get; init; }

    [Required]
    public DateOnly? Date { get; init; }

    [Required, DecimalRange("0", "9999999999999")]
    public decimal? Price { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string? Currency { get; init; }
}

public sealed class FxRateUpsertRequest
{
    /// <summary>"USD-XXX": units of XXX per 1 USD. Only USD-based pairs are accepted (base currency is USD).</summary>
    [Required, RegularExpression("^USD-[A-Z]{3}$", ErrorMessage = "pair must look like USD-ZAR (USD base).")]
    public string? Pair { get; init; }

    [Required]
    public DateOnly? Date { get; init; }

    [Required, DecimalRange("0.00000001", "999999999")]
    public decimal? Rate { get; init; }
}

public sealed record InstrumentResponse(string Isin, string Symbol, string Name, string Currency);
