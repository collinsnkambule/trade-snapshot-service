namespace TradeSnapshot.Domain.Ingestion;

/// <summary>
/// Rollout switch for duplicate/correction handling.
/// </summary>
public enum CorrectionHandlingMode
{
    /// <summary>
    /// Kill switch / legacy-safe behaviour: first write wins. Exact re-sends are acknowledged as duplicates;
    /// any other message for a known external_ref is rejected (409) for manual review. Never auto-corrects.
    /// </summary>
    Disabled = 0,

    /// <summary>Behaves exactly like <see cref="Disabled"/>, but also evaluates and logs what <see cref="Enabled"/> would have done.</summary>
    Shadow = 1,

    /// <summary>Full behaviour: re-sends are ignored, later as_of corrections create a new effective version.</summary>
    Enabled = 2,
}

public enum IngestionOutcome
{
    /// <summary>First time this external_ref was seen; version 1 created.</summary>
    Created,

    /// <summary>Later as_of with changed economics; a new version became effective.</summary>
    Corrected,

    /// <summary>Re-send of something already applied (or a later as_of with no economic change). Nothing written to the trade.</summary>
    Duplicate,

    /// <summary>Older as_of than the effective version (out-of-order delivery). Ignored.</summary>
    Stale,

    /// <summary>Contradicts what we already hold (e.g. same as_of, different content). Needs a human.</summary>
    Conflict,

    /// <summary>Failed business validation (unknown account/instrument, immutable field changed).</summary>
    Rejected,
}

public static class IngestionReasons
{
    public const string NewTrade = "NEW_TRADE";
    public const string LaterAsOfChangedEconomics = "LATER_AS_OF_CHANGED_ECONOMICS";
    public const string SameAsOfSameContent = "SAME_AS_OF_SAME_CONTENT";
    public const string LaterAsOfNoChange = "LATER_AS_OF_NO_ECONOMIC_CHANGE";
    public const string OlderAsOf = "OLDER_AS_OF_THAN_EFFECTIVE_VERSION";
    public const string SameAsOfDifferentContent = "SAME_AS_OF_DIFFERENT_CONTENT";
    public const string ImmutableFieldChanged = "IMMUTABLE_FIELD_CHANGED";
    public const string ExactResend = "EXACT_RESEND_OF_CURRENT_VERSION";
    public const string ManualReviewRequired = "CORRECTION_HANDLING_DISABLED_MANUAL_REVIEW_REQUIRED";
    public const string UnknownAccount = "UNKNOWN_ACCOUNT";
    public const string UnknownInstrument = "UNKNOWN_INSTRUMENT";
}
