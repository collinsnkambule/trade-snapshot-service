using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.Api.Observability;

/// <summary>
/// Source-generated, strongly-typed log events. Stable EventIds and named properties (ExternalRef, AccountId,
/// Outcome, ...) make every event filterable/alertable in a log platform without parsing message text, and the
/// generator avoids boxing/formatting cost when the level is disabled.
/// </summary>
internal static partial class Log
{
    // 1xxx: ingestion
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Trade {ExternalRef} for {AccountId} ingested: {Outcome} ({Reason}) version={Version} as_of={AsOfUtc} mode={Mode} in {ElapsedMs}ms")]
    public static partial void TradeIngested(ILogger logger, string externalRef, string accountId, IngestionOutcome outcome,
        string reason, int? version, string asOfUtc, CorrectionHandlingMode mode, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Trade {ExternalRef} for {AccountId} not applied: {Outcome} ({Reason}) as_of={AsOfUtc} mode={Mode}. Needs attention upstream or manual review.")]
    public static partial void TradeNotApplied(ILogger logger, string externalRef, string accountId, IngestionOutcome outcome,
        string reason, string asOfUtc, CorrectionHandlingMode mode);

    /// <summary>ISO-8601 round-trip text, so the structured property is identical whatever the host's culture.</summary>
    public static string Iso(DateTime utc) => utc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Shadow decision for {ExternalRef}: applied {AppliedOutcome}, full correction handling would have produced {ShadowOutcome} ({ShadowReason}). Diverged={Diverged}")]
    public static partial void ShadowDecision(ILogger logger, string externalRef, IngestionOutcome appliedOutcome,
        IngestionOutcome shadowOutcome, string shadowReason, bool diverged);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Concurrent write detected for trade {ExternalRef} (attempt {Attempt}/{MaxAttempts}); re-reading and re-deciding")]
    public static partial void ConcurrentWriteRetry(ILogger logger, string externalRef, int attempt, int maxAttempts);

    // 2xxx: snapshots
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Snapshot for {AccountId} on {ValuationDate} (known_at={KnownAtUtc}) produced {PositionCount} positions, total {TotalValueUsd} USD in {ElapsedMs:0.0}ms")]
    public static partial void SnapshotProduced(ILogger logger, string accountId, DateOnly valuationDate, DateTime? knownAtUtc,
        int positionCount, decimal totalValueUsd, double elapsedMs);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Snapshot for {AccountId} on {ValuationDate} has {UnpricedPositionCount} position(s) without a price or FX rate; totals exclude them")]
    public static partial void SnapshotHasUnpricedPositions(ILogger logger, string accountId, DateOnly valuationDate, int unpricedPositionCount);

    // 3xxx: reference data
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Price upserted for {Isin} on {PriceDate}: {Price} {Currency}")]
    public static partial void PriceUpserted(ILogger logger, string isin, DateOnly priceDate, decimal price, string currency);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "FX rate upserted for {Pair} on {RateDate}: {Rate}")]
    public static partial void FxRateUpserted(ILogger logger, string pair, DateOnly rateDate, decimal rate);

    // 9xxx: infrastructure
    [LoggerMessage(EventId = 9001, Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0}ms")]
    public static partial void RequestCompleted(ILogger logger, string method, string path, int statusCode, double elapsedMs);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error, Message = "Unhandled exception processing {Method} {Path}")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "Database unavailable after retries processing {Method} {Path}")]
    public static partial void DatabaseUnavailable(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Information, Message = "Correction handling configured: Mode={Mode}, PilotAccounts=[{PilotAccounts}]")]
    public static partial void CorrectionHandlingConfigured(ILogger logger, CorrectionHandlingMode mode, string pilotAccounts);
}
