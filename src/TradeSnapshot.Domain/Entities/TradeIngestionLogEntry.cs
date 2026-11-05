using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.Domain.Entities;

/// <summary>
/// One row per inbound trade message, whatever the outcome (including duplicates and rejections).
/// Gives operations a queryable record of what upstream sent and what we decided.
/// </summary>
public class TradeIngestionLogEntry
{
    public long Id { get; set; }
    public string ExternalRef { get; set; } = default!;
    public string AccountCode { get; set; } = default!;
    public DateTime AsOfUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public IngestionOutcome Outcome { get; set; }
    public string Reason { get; set; } = default!;
    public CorrectionHandlingMode Mode { get; set; }

    /// <summary>In Shadow mode: the outcome the full correction-handling logic would have produced.</summary>
    public IngestionOutcome? ShadowOutcome { get; set; }

    public long? TradeId { get; set; }
    public Trade? Trade { get; set; }
    public int? Version { get; set; }
    public byte[]? Fingerprint { get; set; }
    public string? CorrelationId { get; set; }
}
