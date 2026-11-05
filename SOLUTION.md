# SOLUTION — Trade Ingestion & Portfolio Snapshot Service

This document explains the design, the trade-offs, and the assumptions behind the implementation. Run
instructions are in [README.md](README.md).

## 1. Shape of the solution

```
src/
  TradeSnapshot.Domain          Entities + IngestionPolicy (pure rules, no EF/HTTP dependencies)
  TradeSnapshot.Infrastructure  EF Core DbContext, configurations, migrations, seed data, snapshot reader (ADO.NET)
  TradeSnapshot.Api             ASP.NET Core controllers, contracts, ingestion orchestration, logging/metrics, rollout gate
sql/
  usp_GetPortfolioSnapshot.sql  Task 2 artifact (also deployed by the migration and called by the API)
  snapshot_indexes.sql          Supporting index DDL (idempotent) with access-path notes
  schema.sql                    Idempotent full schema script generated from the migrations
  examples.sql                  Example EXEC calls and audit queries
tests/
  TradeSnapshot.IntegrationTests  One end-to-end test: HTTP -> rules -> SQL Server -> stored proc -> HTTP
scripts/
  demo.ps1 / demo.http          Scripted and manual demos
```

Why three projects rather than one: the rule "is this a re-send, a correction or a conflict?" is the heart of
the service and is likely to be reused by a queue consumer or batch loader later. Keeping it in a dependency-free
`Domain` project (`IngestionPolicy`) makes it host-agnostic and unit-testable without a database. I stopped short
of a full Clean Architecture split (separate Application project, repositories, MediatR) because, at this size,
those layers would add indirection without adding protection. The orchestration service lives in the API project.

## 2. Data model

| Table | Purpose |
|---|---|
| `Accounts`, `Instruments` | Reference data. `Instruments.Currency` is the trade/price currency. |
| `InstrumentPrices` | End-of-day close per instrument per date. PK `(InstrumentId, PriceDate)`. |
| `FxRates` | `USD -> quote` rates per date. PK `(BaseCurrency, QuoteCurrency, RateDate)`. |
| `Trades` | **Head row** per `external_ref`: immutable identity (account, instrument), current version pointer, latest `as_of`, `rowversion`. |
| `TradeVersions` | **Append-only, effective-dated** economics (side, qty, price, trade date) keyed by `(TradeId, AsOfUtc)` with a SHA-256 fingerprint. This is the audit trail and what point-in-time queries read. |
| `TradeIngestionLog` | One row per inbound message **whatever the outcome** (including duplicates and rejections), with mode, shadow outcome, reason and correlation id. |

Key decisions:

* **Corrections never update in place.** A correction inserts a new `TradeVersions` row and moves the head pointer.
  "Current state" is the latest version, and "state as known at T" is the latest version with `AsOfUtc <= T`. Both
  come from the same rows, so the audit trail can't drift from the data.
* **Account and instrument are identity, not economics.** A correction that changes them is rejected (422
  `IMMUTABLE_FIELD_CHANGED`). Re-booking to another account should be a cancel plus a new trade upstream, so that
  each account's history stays explainable.
* **Fingerprint over business content, not raw JSON.** Decimals are normalised (`120` equals `120.000`), so
  formatting noise in upstream payloads is not mistaken for a change.
* **Precision.** Quantity and price are `decimal(19,6)` and FX is `decimal(19,8)`. Intermediate arithmetic in SQL
  keeps at least 10dp; operands are cast explicitly because SQL Server truncates a quotient's scale to 6 when operand
  precision is high. Money is rounded to **2dp, half away from zero, only in the final projection**. Totals are
  summed from unrounded values, so they never drift.
* **Culture independence.** The integration test caught a real locale bug on a machine whose decimal separator is a
  comma: `[Range(typeof(decimal), "0.000001", ...)]` parses its limits in the *current* culture, so every request
  threw. `DecimalRangeAttribute` parses limits and values in the invariant culture, and logged timestamps are emitted
  as ISO-8601 text, so behaviour doesn't depend on the host's regional settings.
* **Timestamps** are stored as UTC `datetime2(7)`. A value converter marks them `DateTimeKind.Utc` on read, so the
  API always emits `...Z`.

## 3. Duplicate and correction handling

`IngestionPolicy.Decide(mode, existing, incoming)` is a pure function. With correction handling **Enabled**:

| Situation | Outcome | HTTP | Effect |
|---|---|---|---|
| New `external_ref` | `CREATED` | 201 | Version 1 |
| Same `as_of` as a stored version, same content | `DUPLICATE` | 200 | Nothing applied |
| Same `as_of` as a stored version, **different** content | `CONFLICT` | 409 | Nothing applied; needs a human |
| `as_of` older than the effective version (not seen before) | `STALE` | 200 | Ignored (never roll back) |
| Later `as_of`, account/instrument changed | `REJECTED` | 422 | Ignored |
| Later `as_of`, no economic change | `DUPLICATE` | 200 | Nothing applied |
| Later `as_of`, changed economics | `CORRECTED` | 200 | Version n+1 becomes effective |
| Unknown account / instrument | `REJECTED` | 422 | Logged in `TradeIngestionLog` |

Duplicates and stale messages return **2xx on purpose**. The upstream system re-sends on retries, and a 4xx would
make it keep retrying or raise false alarms. The response body still says exactly what happened.

**Concurrency.** Every write path is a single `SaveChanges` (one transaction) that adds the version, moves the head
pointer and writes the audit row. Races are caught by the database, not by locks:

* two first-time submissions of a ref: the unique index `UX_Trades_ExternalRef` lets one through;
* two corrections of the same trade: the `rowversion` on `Trades` plus unique `(TradeId, AsOfUtc)` /
  `(TradeId, Version)` let one through.

The loser gets a `DbUpdateException`. The service clears its change tracker, re-reads and re-decides (up to 3
attempts), which normally turns it into a `DUPLICATE` or `STALE`. This keeps throughput high without a serializable
transaction. EF's `EnableRetryOnFailure` handles transient SQL faults separately, and if the database is still
unreachable after retries the API returns **503 with Retry-After**, which is safe because ingestion is idempotent.

## 4. Safe rollout / disablement

Mechanism: the `TradeIngestion:CorrectionHandling` config section, read via `IOptionsMonitor`, so changes in any
reloadable configuration source take effect on the next request **without a redeploy**:

```json
"TradeIngestion": { "CorrectionHandling": { "Mode": "Enabled", "PilotAccounts": [] } }
```

| Mode | Behaviour |
|---|---|
| `Disabled` (kill switch; also the code default if config is missing) | First write wins. Exact re-sends of the current version are acknowledged as `DUPLICATE`; anything else for a known ref is `CONFLICT` (409) for manual review. Never auto-corrects, never double-counts. |
| `Shadow` | Applies the `Disabled` behaviour **and** evaluates the full logic, logging `ShadowDecision` (EventId 1003), storing `ShadowOutcome` on the audit row and incrementing `trade_ingestion.shadow_divergences`. |
| `Enabled` | Full behaviour. With `PilotAccounts` set, only those accounts are Enabled and everyone else runs in Shadow. |

Suggested rollout: deploy with `Disabled`, then `Shadow` and compare shadow decisions against what operations did by
hand, then `Enabled` for one or two pilot accounts, then everyone. Rollback is a config flip back to
`Disabled`/`Shadow`. Every mode writes the same tables in the same shape, so switching back and forth needs no data
migration. `GET /api/v1/admin/correction-handling?account=ACC-001` shows the effective mode.

Why not `Microsoft.FeatureManagement`: a boolean flag can't express "observe but don't act", and Shadow is the most
valuable rollout step here. In a codebase that already standardises on FeatureManagement or LaunchDarkly, the same
three-state switch maps onto a variant flag with account targeting.

The demo `appsettings.json` ships with `Enabled` so the demo shows the full behaviour. A production appsettings would
ship with `Disabled`.

## 5. Observability

* **Structured JSON logs** (built-in console JSON formatter, scopes on). Source-generated `[LoggerMessage]` events
  with stable EventIds and named properties: `1001 TradeIngested`, `1002 TradeNotApplied`, `1003 ShadowDecision`,
  `1004 ConcurrentWriteRetry`, `2001 SnapshotProduced`, `2002 SnapshotHasUnpricedPositions`, `9001 RequestCompleted`,
  `9002/9003` failures. Every line carries `CorrelationId` (from `X-Correlation-ID` or generated; echoed back and
  validated against log forging), plus `ExternalRef`/`AccountId` scopes during ingestion.
* **Persisted audit**: `TradeIngestionLog` answers "what did upstream send at 10:15 and what did we do?" with SQL,
  long after logs have rotated. It's exposed at `GET /api/v1/trades/{ref}` and `GET /api/v1/accounts/{id}/events`.
* **Metrics** (`System.Diagnostics.Metrics`, OpenTelemetry-compatible): `trade_ingestion.messages{outcome,mode}`,
  `trade_ingestion.duration`, `trade_ingestion.shadow_divergences`. Alert on spikes in `CONFLICT`/`REJECTED`. You
  can see them locally with `dotnet-counters monitor --counters TradeSnapshot.Ingestion`.
* **Health**: `/health/live` (process) and `/health/ready` (database).
* **Errors**: RFC 7807 ProblemDetails everywhere, carrying `correlation_id`; validation errors use the snake_case
  field names the caller sent.

## 6. Snapshot and valuation rules (Task 1 API and Task 2 SQL)

The API calls `dbo.usp_GetPortfolioSnapshot`, so **one implementation** of the valuation rules serves both the UI/API
and SQL-based reporting, and the integration test exercises the SQL itself.

Assumptions:

* **Base currency USD.** `usd = local / rate`, where `rate` is `USD-XXX` (units of XXX per 1 USD). USD instruments
  use rate 1. A single pair (USD-ZAR, with a JSE-listed instrument) demonstrates conversion.
* **Valuation date rules.** Trades with `TradeDate <= @ValuationDate` count. Price and FX are the **latest on or
  before** the date (carry-forward over weekends/holidays) and flagged `STALE_PRICE`/`STALE_FX_RATE` when older than
  the date. If there is none, the position is reported with NULL values and `MISSING_PRICE`/`MISSING_FX_RATE`,
  excluded from totals and counted in `unpriced_position_count`. A missing price is shown as missing, never as zero.
* **Version selection happens before the trade-date filter**, because a correction may move the trade date.
* **Knowledge cutoff (`known_at`).** It is optional. When given, only versions with `as_of <= known_at` count, so a
  report can be reproduced exactly as it was at that time (bitemporal: valuation date x knowledge time).
* **Unit cost** is the weighted average cost in instrument currency: total buy cost / total buy quantity for a net
  long position (total sell proceeds / sell quantity for a net short). Sells reduce quantity at that average.
  Unrealised P/L = `(price - unit cost) x quantity / fx`. Flat positions are omitted.
  * *Simplification:* this is "average of all acquisitions", not a true running moving average (which resets when a
    position goes flat and is path-dependent when sells sit between buys). It also converts cost at the valuation
    date's FX, not historical FX. In production I'd keep a persisted position/lot ledger updated incrementally at
    ingestion (and on corrections, by replaying the affected instrument from the corrected trade date). The snapshot
    would then read a pre-computed cost basis instead of deriving it.
* **Cash** isn't modelled. `cash_usd` is returned as 0 and `total_value_usd` equals market value. Adding a
  `CashMovements` table (type `FEE`/`CASH_ADJUSTMENT`, currency, amount, effective-dated the same way) would add one
  more CTE and a line in the account summary.

### Task 2: how the SQL handles corrections and cutoffs

```
RankedVersions  : Trades(AccountId) JOIN TradeVersions WHERE AsOfUtc <= @Cutoff,
                  ROW_NUMBER() OVER (PARTITION BY TradeId ORDER BY AsOfUtc DESC)
EffectiveTrades : VersionRank = 1 AND TradeDate <= @ValuationDate
Holdings        : one GROUP BY pass -> net qty, buy qty/cost, sell qty/proceeds
Costed          : average unit cost
#Positions      : + as-of OUTER APPLY TOP(1) price and FX (backward index seeks), then valued in one UPDATE
Result set 1    : instrument rows        Result set 2 : account totals from #Positions (no second pass over trades)
```

* Set-based and parameterised (`@AccountCode`, `@ValuationDate`, `@KnownAtUtc`). There are no cursors and no dynamic
  SQL.
* `@KnownAtUtc = NULL` is replaced by `9999-12-31` before use, so the predicate stays a SARGable range instead of
  the `(@p IS NULL OR col <= @p)` catch-all that defeats seeks.
* The small per-account working set is materialised once into `#Positions`, so both result sets come from one pass.

### Indexes (DDL in `sql/snapshot_indexes.sql`, created by the migration)

| Access path | Index |
|---|---|
| AccountCode -> Id | `UX_Accounts_AccountCode` (unique) |
| Trades for an account | `IX_Trades_AccountId INCLUDE (InstrumentId)` |
| Latest version per trade within the cutoff | `UX_TradeVersions_TradeId_AsOfUtc (TradeId, AsOfUtc DESC) INCLUDE (Side, Quantity, Price, TradeDate)`: covering, and already in `ROW_NUMBER` order, so there's no sort |
| Latest price on/before date | existing clustered `PK_InstrumentPrices (InstrumentId, PriceDate)` is sufficient: backward seek for `TOP (1)` |
| Latest FX on/before date | existing clustered `PK_FxRates (BaseCurrency, QuoteCurrency, RateDate)` is sufficient |

## 7. Performance and maintainability at higher volume

**Indexing and SARGability**
* Keep predicates on bare columns (`AsOfUtc <= @Cutoff`, `TradeDate <= @ValuationDate`). Don't wrap columns in
  functions such as `CAST(AsOfUtc AS date)`, and keep parameter types matching column types (`varchar` vs
  `nvarchar` mismatches cause implicit conversions and scans; parameters are typed explicitly in
  `PortfolioSnapshotReader`).
* Covering indexes only for hot paths. Watch `sys.dm_db_index_usage_stats` and the Query Store, and drop unused
  indexes because each one taxes ingestion.
* For accounts with very skewed sizes, parameter sniffing can pick a plan that suits one account badly. Options
  include Query Store plan forcing, `OPTIMIZE FOR UNKNOWN`, or `OPTION (RECOMPILE)` for a low-frequency reporting
  procedure.
* If accounts hold many trades with long version chains, an `OUTER APPLY (SELECT TOP (1) ... ORDER BY AsOfUtc DESC)`
  per trade (one seek per trade) can beat `ROW_NUMBER` over all versions. Measure both with the Query Store.

**Avoid recomputing history**
* Snapshots for closed dates are immutable unless a correction lands for that date. Persist a daily
  `PositionSnapshot` table (account, instrument, date, qty, cost) built by a nightly job, and only recompute from the
  earliest affected trade date when a correction arrives. Today's snapshot = yesterday's positions + today's
  effective versions.
* Move the average-cost calculation to an incrementally maintained position ledger (see section 6).

**Partitioning and archival**
* Partition `TradeVersions` and `TradeIngestionLog` monthly on `AsOfUtc` / `ReceivedAtUtc`, with aligned indexes.
  Old months can then be switched out to an archive table (metadata-only) and compressed (`PAGE`, or clustered
  columnstore for analytics).
* `TradeIngestionLog` grows with every re-send. Keep, say, 13 months hot and archive older data to cheaper storage.
  Retention follows the firm's record-keeping policy.
* Price/FX history: partition by date if instrument coverage is large, and keep only what valuation needs hot.
* Run heavy reporting on a readable secondary (Always On AG) or a reporting replica, not on the ingestion primary.

**Maintainability**
* The SQL artifact is a single reviewed file under `sql/`, embedded into the migration, so what is reviewed is what
  is deployed. A change ships as a new migration that re-applies the `CREATE OR ALTER`. `sql/schema.sql` is an
  idempotent script for DBA-controlled environments.
* Add tSQLt tests for the procedure's edge cases (missing price, short positions, cutoff boundaries) next to it.

## 8. Integrating into .NET Framework 4.8 vs modern .NET

| Concern | .NET 8 (this solution) | .NET Framework 4.8 environment |
|---|---|---|
| Web host | ASP.NET Core, Kestrel, built-in DI, middleware | ASP.NET Web API 2 / MVC 5 on IIS; DI via Autofac/Unity/SimpleInjector; cross-cutting concerns as `DelegatingHandler`s / action filters / `IExceptionHandler` |
| Data access | EF Core 8 (`DateOnly`, descending/included indexes, `IsRowVersion`, execution strategies) | EF Core stops at 3.1 on .NET Framework (out of support). Use **EF6** (no `DateOnly`, no fluent `INCLUDE`/descending indexes, so put those in SQL migrations; `DbConfiguration` for SqlAzureExecutionStrategy) or **Dapper** with the same stored procedure. Calling the procedure keeps the logic portable across both. |
| SQL client | `Microsoft.Data.SqlClient` | Prefer `Microsoft.Data.SqlClient` too (supports .NET Framework); `System.Data.SqlClient` is legacy |
| Schema | EF Core migrations + idempotent script | EF6 migrations or DbUp/SSDT; the idempotent `sql/*.sql` scripts work unchanged |
| Config / rollout | `IOptionsMonitor` hot reload | `web.config` appSettings (editing restarts the app domain) or an external flag service; wrap behind the same `ICorrectionHandlingGate` interface |
| Logging | `ILogger` + source-generated `[LoggerMessage]`, JSON console, `System.Diagnostics.Metrics` | Serilog (or log4net/NLog) with JSON sinks; correlation via `LogContext` in a `DelegatingHandler`; metrics via OpenTelemetry .NET (supports net462+) or perf counters/App Insights. `Microsoft.Extensions.Logging` itself also runs on .NET Framework, which eases a later port. |
| Async | Async end-to-end | Async works, but watch `SynchronizationContext` deadlocks (`ConfigureAwait(false)` in libraries) |
| Dependencies | `PackageReference`, central versions | `packages.config` is common; migrate to `PackageReference`. Binding redirects are a frequent source of pain. |
| Language | C# 12 (primary constructors, collection expressions, records) | C# 7.3 by default. `Domain` uses newer syntax, but its logic has no framework dependencies, so it's straightforward to compile for `netstandard2.0`. |

Practical path: put the rules (`TradeSnapshot.Domain`) in a `netstandard2.0` library both stacks can reference, keep
the valuation in the stored procedure, and run the new ingestion as a separate .NET 8 service in front of the legacy
app (strangler pattern), rather than back-porting it into the 4.8 codebase.

## 9. What I'd do next

* Message-based ingestion (Service Bus/Kafka consumer reusing `TradeIngestionService`) with an outbox for
  downstream "trade changed" events.
* AuthN/Z (client credentials for upstream, per-account authorisation for the UI) and rate limiting.
* Cancellations as a first-class status (`CANCELLED` version) instead of only quantity corrections.
* Unit tests for `IngestionPolicy`'s decision table, plus tSQLt tests for the procedure.
* Cash movements and a small Blazor UI listing the snapshot and recent events.
