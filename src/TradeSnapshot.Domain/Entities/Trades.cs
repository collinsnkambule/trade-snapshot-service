namespace TradeSnapshot.Domain.Entities;

public enum TradeSide : byte
{
    Buy = 1,
    Sell = 2,
}

/// <summary>
/// Head record for a trade, keyed by the upstream <see cref="ExternalRef"/>. Identity fields
/// (account, instrument) are immutable; economics live on <see cref="TradeVersion"/> rows.
/// </summary>
public class Trade
{
    public long Id { get; set; }
    public string ExternalRef { get; set; } = default!;
    public int AccountId { get; set; }
    public int InstrumentId { get; set; }

    /// <summary>Version number of the currently effective (latest as_of) version.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>The as_of of the currently effective version. Used to classify incoming events quickly.</summary>
    public DateTime LatestAsOfUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Optimistic concurrency token: two corrections racing for the same trade cannot both win.</summary>
    public byte[] RowVersion { get; set; } = default!;

    public Account Account { get; set; } = default!;
    public Instrument Instrument { get; set; } = default!;
    public List<TradeVersion> Versions { get; set; } = new();
}

/// <summary>
/// Append-only, effective-dated version of a trade's economics. Never updated or deleted: the set of
/// versions is the audit trail, and "latest version with AsOfUtc &lt;= cutoff" gives point-in-time views.
/// </summary>
public class TradeVersion
{
    public long Id { get; set; }
    public long TradeId { get; set; }
    public int Version { get; set; }

    public TradeSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public DateOnly TradeDate { get; set; }

    /// <summary>Upstream as_of timestamp (UTC): when the upstream system asserted this version.</summary>
    public DateTime AsOfUtc { get; set; }

    /// <summary>When this service received and persisted the version (UTC).</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>SHA-256 of the canonical economic content; used to tell re-sends from conflicting messages.</summary>
    public byte[] Fingerprint { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public Trade Trade { get; set; } = default!;
}
