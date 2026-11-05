using System.Diagnostics;
using System.Diagnostics.Metrics;
using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.Api.Observability;

/// <summary>
/// System.Diagnostics.Metrics instruments (OpenTelemetry-compatible; view locally with `dotnet-counters monitor
/// --counters TradeSnapshot.Ingestion`). Lets operations alert on, e.g., a spike in Conflict/Stale outcomes or a
/// Shadow-mode divergence rate before switching correction handling on.
/// </summary>
public sealed class IngestionMetrics
{
    public const string MeterName = "TradeSnapshot.Ingestion";

    private readonly Counter<long> _messages;
    private readonly Counter<long> _shadowDivergences;
    private readonly Histogram<double> _duration;

    public IngestionMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _messages = meter.CreateCounter<long>("trade_ingestion.messages", unit: "{message}", description: "Inbound trade messages by outcome and mode.");
        _shadowDivergences = meter.CreateCounter<long>("trade_ingestion.shadow_divergences", unit: "{message}", description: "Shadow-mode messages where full correction handling would have decided differently.");
        _duration = meter.CreateHistogram<double>("trade_ingestion.duration", unit: "ms", description: "Time to classify and persist an inbound trade message.");
    }

    public void Record(IngestionOutcome outcome, CorrectionHandlingMode mode, double elapsedMs)
    {
        var tags = new TagList { { "outcome", outcome.ToString() }, { "mode", mode.ToString() } };
        _messages.Add(1, tags);
        _duration.Record(elapsedMs, tags);
    }

    public void RecordShadowDivergence(IngestionOutcome applied, IngestionOutcome shadow) =>
        _shadowDivergences.Add(1, new TagList { { "applied", applied.ToString() }, { "shadow", shadow.ToString() } });
}
