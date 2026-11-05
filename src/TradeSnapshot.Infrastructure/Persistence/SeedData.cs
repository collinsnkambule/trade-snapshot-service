using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Infrastructure.Persistence;

/// <summary>Reference data seeded by the initial migration (EF HasData). Trades are created through the API.</summary>
internal static class SeedData
{
    private static readonly DateTime SeededAt = new(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly D20250301 = new(2025, 3, 1);
    private static readonly DateOnly D20250303 = new(2025, 3, 3);

    public static readonly Account[] Accounts =
    [
        new() { Id = 1, AccountCode = "ACC-001", Name = "Demo Growth Portfolio", BaseCurrency = "USD" },
        new() { Id = 2, AccountCode = "ACC-002", Name = "Demo Income Portfolio", BaseCurrency = "USD" },
    ];

    public static readonly Instrument[] Instruments =
    [
        new() { Id = 1, Isin = "US0378331005", Symbol = "AAPL", Name = "Apple Inc.", Currency = "USD" },
        new() { Id = 2, Isin = "US5949181045", Symbol = "MSFT", Name = "Microsoft Corp.", Currency = "USD" },
        // ZAR-quoted instrument to demonstrate USD conversion through the USD-ZAR pair.
        new() { Id = 3, Isin = "ZAE000015889", Symbol = "NPN", Name = "Naspers Ltd (JSE)", Currency = "ZAR" },
    ];

    // Anonymous objects so the seed does not need the navigation property populated.
    public static readonly object[] Prices =
    [
        new { InstrumentId = 1, PriceDate = D20250301, ClosePrice = 186.00m, UpdatedAtUtc = SeededAt },
        new { InstrumentId = 1, PriceDate = D20250303, ClosePrice = 188.50m, UpdatedAtUtc = SeededAt },
        new { InstrumentId = 2, PriceDate = D20250301, ClosePrice = 397.00m, UpdatedAtUtc = SeededAt },
        new { InstrumentId = 3, PriceDate = D20250301, ClosePrice = 4500.00m, UpdatedAtUtc = SeededAt },
    ];

    public static readonly FxRate[] FxRates =
    [
        new() { BaseCurrency = "USD", QuoteCurrency = "ZAR", RateDate = D20250301, Rate = 18.72m, UpdatedAtUtc = SeededAt },
    ];
}
