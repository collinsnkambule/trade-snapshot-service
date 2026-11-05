using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Domain.Ingestion;

/// <summary>What we already know about a trade (null when the external_ref is new).</summary>
public sealed record ExistingTradeState(
    int AccountId,
    int InstrumentId,
    int CurrentVersion,
    DateTime LatestAsOfUtc,
    byte[] CurrentFingerprint,
    IReadOnlyDictionary<DateTime, byte[]> FingerprintsByAsOf);

/// <summary>The inbound event after reference-data resolution.</summary>
public sealed record IncomingTrade(int AccountId, int InstrumentId, DateTime AsOfUtc, byte[] Fingerprint);

public sealed record IngestionDecision(IngestionOutcome Outcome, string Reason, int? NewVersion = null)
{
    public bool WritesNewVersion => Outcome is IngestionOutcome.Created or IngestionOutcome.Corrected;
}

/// <summary>
/// Pure, side-effect-free classification of an inbound trade event. Kept free of EF/HTTP so the rules are
/// trivially unit-testable and identical whichever host (API, queue consumer, batch) calls them.
/// </summary>
public static class IngestionPolicy
{
    public static IngestionDecision Decide(CorrectionHandlingMode mode, ExistingTradeState? existing, IncomingTrade incoming) =>
        mode == CorrectionHandlingMode.Enabled
            ? DecideWithCorrections(existing, incoming)
            : DecideFirstWriteWins(existing, incoming);

    /// <summary>Full behaviour: as_of ordering decides re-send vs correction.</summary>
    private static IngestionDecision DecideWithCorrections(ExistingTradeState? existing, IncomingTrade incoming)
    {
        if (existing is null)
            return new(IngestionOutcome.Created, IngestionReasons.NewTrade, NewVersion: 1);

        // Exactly this as_of was processed before: a re-send if the content matches, otherwise upstream
        // has told us two different things at the same instant and a human must decide.
        if (existing.FingerprintsByAsOf.TryGetValue(incoming.AsOfUtc, out var seen))
        {
            return seen.AsSpan().SequenceEqual(incoming.Fingerprint)
                ? new(IngestionOutcome.Duplicate, IngestionReasons.SameAsOfSameContent)
                : new(IngestionOutcome.Conflict, IngestionReasons.SameAsOfDifferentContent);
        }

        // Older than what is effective: an out-of-order re-send of a superseded version. Never roll back.
        if (incoming.AsOfUtc < existing.LatestAsOfUtc)
            return new(IngestionOutcome.Stale, IngestionReasons.OlderAsOf);

        // Later as_of: a correction. Account/instrument are identity, not economics: moving a trade to another
        // book must be a cancel + new booking upstream so both accounts' histories stay explainable.
        if (incoming.AccountId != existing.AccountId || incoming.InstrumentId != existing.InstrumentId)
            return new(IngestionOutcome.Rejected, IngestionReasons.ImmutableFieldChanged);

        if (existing.CurrentFingerprint.AsSpan().SequenceEqual(incoming.Fingerprint))
            return new(IngestionOutcome.Duplicate, IngestionReasons.LaterAsOfNoChange);

        return new(IngestionOutcome.Corrected, IngestionReasons.LaterAsOfChangedEconomics, existing.CurrentVersion + 1);
    }

    /// <summary>Kill-switch behaviour: never applies a second version; anything but an exact re-send needs review.</summary>
    private static IngestionDecision DecideFirstWriteWins(ExistingTradeState? existing, IncomingTrade incoming)
    {
        if (existing is null)
            return new(IngestionOutcome.Created, IngestionReasons.NewTrade, NewVersion: 1);

        return existing.CurrentFingerprint.AsSpan().SequenceEqual(incoming.Fingerprint)
            ? new(IngestionOutcome.Duplicate, IngestionReasons.ExactResend)
            : new(IngestionOutcome.Conflict, IngestionReasons.ManualReviewRequired);
    }
}
