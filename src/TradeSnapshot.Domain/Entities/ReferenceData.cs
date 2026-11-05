namespace TradeSnapshot.Domain.Entities;

/// <summary>A portfolio account owned by the upstream administration system.</summary>
public class Account
{
    public int Id { get; set; }
    public string AccountCode { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string BaseCurrency { get; set; } = "USD";
}

/// <summary>A tradable instrument. <see cref="Currency"/> is the currency trades and prices are quoted in.</summary>
public class Instrument
{
    public int Id { get; set; }
    public string Isin { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Currency { get; set; } = "USD";
}

/// <summary>End-of-day close price in the instrument's own currency.</summary>
public class InstrumentPrice
{
    public int InstrumentId { get; set; }
    public DateOnly PriceDate { get; set; }
    public decimal ClosePrice { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Instrument Instrument { get; set; } = default!;
}

/// <summary>
/// FX rate expressed as units of <see cref="QuoteCurrency"/> per one unit of <see cref="BaseCurrency"/>
/// (e.g. USD-ZAR 18.72 means 1 USD = 18.72 ZAR).
/// </summary>
public class FxRate
{
    public string BaseCurrency { get; set; } = "USD";
    public string QuoteCurrency { get; set; } = default!;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
