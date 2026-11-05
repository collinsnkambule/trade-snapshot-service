using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSnapshot.Api;
using TradeSnapshot.Api.Contracts;
using TradeSnapshot.Domain.Ingestion;

namespace TradeSnapshot.IntegrationTests;

/// <summary>
/// One end-to-end flow through HTTP -> ingestion rules -> SQL Server -> stored procedure -> HTTP:
/// initial submission, exact re-send, later correction, out-of-order stale re-send, conflicting re-send,
/// a second (ZAR) trade, then snapshots for the corrected view and for a point in time before the correction.
/// </summary>
public sealed class TradeLifecycleEndToEndTests(SqlServerApiFactory factory) : IClassFixture<SqlServerApiFactory>
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();

    [Fact]
    public async Task Resends_are_not_double_counted_and_corrections_flow_through_to_the_snapshot()
    {
        var client = factory.CreateClient();

        // 1. Initial submission -> 201 Created, version 1.
        var original = Trade("TRX-1001", "US0378331005", "BUY", quantity: 120m, price: 185.40m, asOf: "2025-03-01T10:15:00Z");
        var created = await Submit(client, original, HttpStatusCode.Created);
        Assert.Equal(IngestionOutcome.Created, created.Outcome);
        Assert.Equal(1, created.Version);

        // 2. Upstream re-sends the identical message -> Duplicate, nothing applied.
        var resent = await Submit(client, original, HttpStatusCode.OK);
        Assert.Equal(IngestionOutcome.Duplicate, resent.Outcome);
        Assert.Equal(1, resent.CurrentVersion);

        // 3. Correction: same external_ref, later as_of, changed quantity and price -> version 2 becomes effective.
        var correction = Trade("TRX-1001", "US0378331005", "BUY", quantity: 100m, price: 185.20m, asOf: "2025-03-01T14:00:00Z");
        var corrected = await Submit(client, correction, HttpStatusCode.OK);
        Assert.Equal(IngestionOutcome.Corrected, corrected.Outcome);
        Assert.Equal(2, corrected.Version);

        // 4. The original arrives again after the correction (late retry) -> still a Duplicate of v1; no rollback.
        var lateResend = await Submit(client, original, HttpStatusCode.OK);
        Assert.Equal(IngestionOutcome.Duplicate, lateResend.Outcome);
        Assert.Equal(2, lateResend.CurrentVersion);

        // 5. An intermediate version we never saw arrives out of order (older than effective) -> Stale, ignored.
        var outOfOrder = Trade("TRX-1001", "US0378331005", "BUY", quantity: 110m, price: 185.30m, asOf: "2025-03-01T12:30:00Z");
        var stale = await Submit(client, outOfOrder, HttpStatusCode.OK);
        Assert.Equal(IngestionOutcome.Stale, stale.Outcome);

        // 6. Same as_of as v2 but different content -> 409 for manual review.
        var contradictory = Trade("TRX-1001", "US0378331005", "BUY", quantity: 999m, price: 185.20m, asOf: "2025-03-01T14:00:00Z");
        var conflict = await client.PostAsJsonAsync("/api/v1/trades", contradictory, Json);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        // 7. A ZAR-quoted instrument, to exercise USD conversion through USD-ZAR 18.72.
        await Submit(client, Trade("TRX-1002", "ZAE000015889", "BUY", quantity: 50m, price: 4450m, asOf: "2025-03-01T10:30:00Z"), HttpStatusCode.Created);

        // 8. Corrected view: AAPL 100 @ avg 185.20 valued at 186.00; NPN 50 @ 4450 ZAR valued at 4500 ZAR.
        var snapshot = await GetSnapshot(client, "/api/v1/accounts/ACC-001/snapshot?date=2025-03-01");
        var aapl = Assert.Single(snapshot.Positions, p => p.Symbol == "AAPL");
        Assert.Equal(100m, aapl.Quantity);
        Assert.Equal(185.20m, aapl.UnitCost);
        Assert.Equal(18_600.00m, aapl.MarketValueUsd);
        Assert.Equal(80.00m, aapl.UnrealizedPnlUsd);
        Assert.Equal(1, aapl.TradeCount); // the re-sends did not create extra trades

        var npn = Assert.Single(snapshot.Positions, p => p.Symbol == "NPN");
        Assert.Equal(225_000.00m, npn.MarketValueLocal);
        Assert.Equal(18.72m, npn.Fx.Rate);
        Assert.Equal(12_019.23m, npn.MarketValueUsd);    // 225 000 / 18.72
        Assert.Equal(133.55m, npn.UnrealizedPnlUsd);     // 2 500 / 18.72

        Assert.Equal(2, snapshot.Totals.PositionCount);
        Assert.Equal(0, snapshot.Totals.UnpricedPositionCount);
        Assert.Equal(30_619.23m, snapshot.Totals.MarketValueUsd);   // 18 600 + 12 019.2308 (summed unrounded)
        Assert.Equal(30_405.68m, snapshot.Totals.CostBasisUsd);     // 18 520 + 11 885.6838
        Assert.Equal(213.55m, snapshot.Totals.UnrealizedPnlUsd);
        Assert.Equal(30_619.23m, snapshot.Totals.TotalValueUsd);

        // 9. Point-in-time view before the 14:00 correction was known: v1 (120 @ 185.40) is effective.
        var asKnownAtNoon = await GetSnapshot(client, "/api/v1/accounts/ACC-001/snapshot?date=2025-03-01&known_at=2025-03-01T12:00:00Z");
        var aaplAtNoon = Assert.Single(asKnownAtNoon.Positions, p => p.Symbol == "AAPL");
        Assert.Equal(120m, aaplAtNoon.Quantity);
        Assert.Equal(22_320.00m, aaplAtNoon.MarketValueUsd);
        Assert.Equal(72.00m, aaplAtNoon.UnrealizedPnlUsd);
        Assert.Equal(34_339.23m, asKnownAtNoon.Totals.TotalValueUsd);

        // 10. Audit trail: two versions retained, every inbound message recorded with its outcome.
        var details = await client.GetFromJsonAsync<TradeDetailsResponse>("/api/v1/trades/TRX-1001", Json);
        Assert.NotNull(details);
        Assert.Equal(2, details.CurrentVersion);
        Assert.Equal(new[] { 2, 1 }, details.Versions.Select(v => v.Version));
        Assert.Equal(
            new[] { IngestionOutcome.Created, IngestionOutcome.Duplicate, IngestionOutcome.Corrected, IngestionOutcome.Duplicate, IngestionOutcome.Stale, IngestionOutcome.Conflict },
            details.IngestionEvents.Reverse().Select(e => e.Outcome));
    }

    private static object Trade(string externalRef, string isin, string side, decimal quantity, decimal price, string asOf) => new
    {
        external_ref = externalRef,
        account_id = "ACC-001",
        instrument = new { isin },
        side,
        quantity,
        price,
        trade_date = "2025-03-01",
        as_of = asOf,
    };

    private static async Task<TradeIngestionResponse> Submit(HttpClient client, object trade, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync("/api/v1/trades", trade, Json);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<TradeIngestionResponse>(body, Json)!;
    }

    private static async Task<PortfolioSnapshotResponse> GetSnapshot(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Snapshot failed with {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<PortfolioSnapshotResponse>(body, Json)!;
    }
}
