using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TradeSnapshot.Api.Observability;
using TradeSnapshot.Domain.Entities;
using TradeSnapshot.Domain.Ingestion;
using TradeSnapshot.Infrastructure.Persistence;

namespace TradeSnapshot.Api.Ingestion;

/// <summary>Validated, normalised inbound trade (transport-agnostic: an HTTP controller or a queue consumer can build it).</summary>
public sealed record TradeSubmission(
    string ExternalRef,
    string AccountCode,
    string? Isin,
    string? Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    DateOnly TradeDate,
    DateTime AsOfUtc,
    string? CorrelationId);

public sealed record IngestionResult(
    string ExternalRef,
    IngestionOutcome Outcome,
    string Reason,
    long? TradeId,
    int? Version,
    int? CurrentVersion,
    DateTime AsOfUtc,
    CorrectionHandlingMode Mode,
    IngestionOutcome? ShadowOutcome);

/// <summary>
/// Orchestrates one inbound trade message: resolve reference data, load what we know, ask the pure
/// <see cref="IngestionPolicy"/> what to do, then persist the version (if any) and the audit row in ONE
/// SaveChanges (one transaction). Concurrency is optimistic: the unique indexes on ExternalRef and
/// (TradeId, AsOfUtc) plus the Trades.RowVersion token turn a race into an exception, and the loser simply
/// re-reads and re-decides, which normally makes it a Duplicate.
/// </summary>
public sealed class TradeIngestionService(
    TradeSnapshotDbContext db,
    ICorrectionHandlingGate gate,
    IngestionMetrics metrics,
    TimeProvider clock,
    ILogger<TradeIngestionService> logger)
{
    private const int MaxAttempts = 3;

    public async Task<IngestionResult> IngestAsync(TradeSubmission submission, CancellationToken ct)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ExternalRef"] = submission.ExternalRef,
            ["AccountId"] = submission.AccountCode,
        });

        var start = Stopwatch.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await TryIngestAsync(submission, ct);
                var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                metrics.Record(result.Outcome, result.Mode, elapsedMs);
                if (result.Outcome is IngestionOutcome.Conflict or IngestionOutcome.Rejected)
                    Log.TradeNotApplied(logger, result.ExternalRef, submission.AccountCode, result.Outcome, result.Reason, Log.Iso(result.AsOfUtc), result.Mode);
                else
                    Log.TradeIngested(logger, result.ExternalRef, submission.AccountCode, result.Outcome, result.Reason, result.Version, Log.Iso(result.AsOfUtc), result.Mode, Math.Round(elapsedMs, 1));

                return result;
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && DbExceptionClassifier.IsLostRace(ex))
            {
                Log.ConcurrentWriteRetry(logger, submission.ExternalRef, attempt, MaxAttempts);
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<IngestionResult> TryIngestAsync(TradeSubmission s, CancellationToken ct)
    {
        var mode = gate.ResolveFor(s.AccountCode);
        var now = clock.GetUtcNow().UtcDateTime;

        var accountId = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountCode == s.AccountCode)
            .Select(a => (int?)a.Id)
            .SingleOrDefaultAsync(ct);
        if (accountId is null)
            return await RecordRejectionAsync(s, mode, IngestionReasons.UnknownAccount, now, ct);

        var instrumentId = await ResolveInstrumentAsync(s, ct);
        if (instrumentId is null)
            return await RecordRejectionAsync(s, mode, IngestionReasons.UnknownInstrument, now, ct);

        var fingerprint = TradeFingerprint.Compute(accountId.Value, instrumentId.Value, s.Side, s.Quantity, s.Price, s.TradeDate);

        var trade = await db.Trades
            .Include(t => t.Versions)
            .SingleOrDefaultAsync(t => t.ExternalRef == s.ExternalRef, ct);

        var existing = trade is null ? null : new ExistingTradeState(
            trade.AccountId,
            trade.InstrumentId,
            trade.CurrentVersion,
            trade.LatestAsOfUtc,
            trade.Versions.Single(v => v.Version == trade.CurrentVersion).Fingerprint,
            trade.Versions.ToDictionary(v => v.AsOfUtc, v => v.Fingerprint));

        var incoming = new IncomingTrade(accountId.Value, instrumentId.Value, s.AsOfUtc, fingerprint);
        var decision = IngestionPolicy.Decide(mode, existing, incoming);

        IngestionDecision? shadow = null;
        if (mode == CorrectionHandlingMode.Shadow)
        {
            shadow = IngestionPolicy.Decide(CorrectionHandlingMode.Enabled, existing, incoming);
            var diverged = shadow.Outcome != decision.Outcome;
            Log.ShadowDecision(logger, s.ExternalRef, decision.Outcome, shadow.Outcome, shadow.Reason, diverged);
            if (diverged) metrics.RecordShadowDivergence(decision.Outcome, shadow.Outcome);
        }

        switch (decision.Outcome)
        {
            case IngestionOutcome.Created:
                trade = new Trade
                {
                    ExternalRef = s.ExternalRef,
                    AccountId = accountId.Value,
                    InstrumentId = instrumentId.Value,
                    CurrentVersion = 1,
                    LatestAsOfUtc = s.AsOfUtc,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                trade.Versions.Add(NewVersion(s, version: 1, fingerprint, now));
                db.Trades.Add(trade);
                break;

            case IngestionOutcome.Corrected:
                var newVersion = decision.NewVersion!.Value;
                trade!.Versions.Add(NewVersion(s, newVersion, fingerprint, now));
                trade.CurrentVersion = newVersion;
                trade.LatestAsOfUtc = s.AsOfUtc;
                trade.UpdatedAtUtc = now; // marks the head modified, so its RowVersion is checked on save
                break;
        }

        // The version this message corresponds to: the one just written, or the existing one it re-sent.
        var relatedVersion = decision.NewVersion
            ?? trade?.Versions.FirstOrDefault(v => v.AsOfUtc == s.AsOfUtc)?.Version;

        db.TradeIngestionLog.Add(new TradeIngestionLogEntry
        {
            ExternalRef = s.ExternalRef,
            AccountCode = s.AccountCode,
            AsOfUtc = s.AsOfUtc,
            ReceivedAtUtc = now,
            Outcome = decision.Outcome,
            Reason = decision.Reason,
            Mode = mode,
            ShadowOutcome = shadow?.Outcome,
            Trade = trade,
            Version = relatedVersion,
            Fingerprint = fingerprint,
            CorrelationId = s.CorrelationId,
        });

        await db.SaveChangesAsync(ct);

        return new IngestionResult(s.ExternalRef, decision.Outcome, decision.Reason, trade?.Id, relatedVersion,
            trade?.CurrentVersion, s.AsOfUtc, mode, shadow?.Outcome);
    }

    private async Task<int?> ResolveInstrumentAsync(TradeSubmission s, CancellationToken ct)
    {
        // ISIN is the authoritative key; symbol is a convenience fallback and must be unambiguous.
        if (!string.IsNullOrEmpty(s.Isin))
        {
            return await db.Instruments.AsNoTracking()
                .Where(i => i.Isin == s.Isin)
                .Select(i => (int?)i.Id)
                .SingleOrDefaultAsync(ct);
        }

        var matches = await db.Instruments.AsNoTracking()
            .Where(i => i.Symbol == s.Symbol)
            .Select(i => i.Id)
            .Take(2)
            .ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task<IngestionResult> RecordRejectionAsync(TradeSubmission s, CorrectionHandlingMode mode, string reason, DateTime now, CancellationToken ct)
    {
        db.TradeIngestionLog.Add(new TradeIngestionLogEntry
        {
            ExternalRef = s.ExternalRef,
            AccountCode = s.AccountCode,
            AsOfUtc = s.AsOfUtc,
            ReceivedAtUtc = now,
            Outcome = IngestionOutcome.Rejected,
            Reason = reason,
            Mode = mode,
            CorrelationId = s.CorrelationId,
        });
        await db.SaveChangesAsync(ct);
        return new IngestionResult(s.ExternalRef, IngestionOutcome.Rejected, reason, null, null, null, s.AsOfUtc, mode, null);
    }

    private static TradeVersion NewVersion(TradeSubmission s, int version, byte[] fingerprint, DateTime now) => new()
    {
        Version = version,
        Side = s.Side,
        Quantity = s.Quantity,
        Price = s.Price,
        TradeDate = s.TradeDate,
        AsOfUtc = s.AsOfUtc,
        ReceivedAtUtc = now,
        Fingerprint = fingerprint,
        CorrelationId = s.CorrelationId,
    };
}
