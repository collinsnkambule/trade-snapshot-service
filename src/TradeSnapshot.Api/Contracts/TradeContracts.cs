using System.ComponentModel.DataAnnotations;
using TradeSnapshot.Domain.Entities;
using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.Api.Contracts;

/// <summary>Inbound trade event from the upstream administration system (snake_case on the wire).</summary>
public sealed class TradeSubmissionRequest : IValidatableObject
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string? ExternalRef { get; init; }

    [Required, StringLength(32, MinimumLength = 1)]
    public string? AccountId { get; init; }

    [Required]
    public InstrumentReference? Instrument { get; init; }

    [Required]
    public TradeSide? Side { get; init; }

    [Required, DecimalRange("0.000001", "9999999999999")]
    public decimal? Quantity { get; init; }

    [Required, DecimalRange("0", "9999999999999")]
    public decimal? Price { get; init; }

    [Required]
    public DateOnly? TradeDate { get; init; }

    /// <summary>ISO 8601 timestamp; normalised to UTC. The upstream assertion time used for ordering versions.</summary>
    [Required]
    public DateTimeOffset? AsOf { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Instrument is not null && string.IsNullOrWhiteSpace(Instrument.Isin) && string.IsNullOrWhiteSpace(Instrument.Symbol))
            yield return new ValidationResult("Either instrument.isin or instrument.symbol is required.", [nameof(Instrument)]);

        if (Quantity is { } q && decimal.Round(q, 6) != q)
            yield return new ValidationResult("At most 6 decimal places are supported.", [nameof(Quantity)]);

        if (Price is { } p && decimal.Round(p, 6) != p)
            yield return new ValidationResult("At most 6 decimal places are supported.", [nameof(Price)]);

        if (TradeDate is { } td && AsOf is { } asOf && td > DateOnly.FromDateTime(asOf.UtcDateTime))
            yield return new ValidationResult("trade_date cannot be after the as_of date.", [nameof(TradeDate)]);
    }
}

public sealed class InstrumentReference
{
    [StringLength(12, MinimumLength = 12)]
    public string? Isin { get; init; }

    [StringLength(16)]
    public string? Symbol { get; init; }
}

public sealed record TradeIngestionResponse(
    string ExternalRef,
    IngestionOutcome Outcome,
    string Reason,
    long? TradeId,
    int? Version,
    int? CurrentVersion,
    DateTime AsOfUtc,
    CorrectionHandlingMode Mode,
    IngestionOutcome? ShadowOutcome);

public sealed record TradeVersionResponse(
    int Version,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateTime AsOfUtc,
    DateTime ReceivedAtUtc,
    bool IsCurrent);

public sealed record TradeDetailsResponse(
    long TradeId,
    string ExternalRef,
    string AccountId,
    string Isin,
    string Symbol,
    int CurrentVersion,
    IReadOnlyList<TradeVersionResponse> Versions,
    IReadOnlyList<IngestionEventResponse> IngestionEvents);

public sealed record IngestionEventResponse(
    string ExternalRef,
    DateTime AsOfUtc,
    DateTime ReceivedAtUtc,
    IngestionOutcome Outcome,
    string Reason,
    CorrectionHandlingMode Mode,
    IngestionOutcome? ShadowOutcome,
    int? Version,
    string? CorrelationId);
