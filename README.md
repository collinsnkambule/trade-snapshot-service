# Trade Ingestion & Portfolio Snapshot Service

ASP.NET Core (.NET 8) Web API that ingests trades from an upstream administration system into SQL Server via
EF Core. It ignores re-sent messages, applies later corrections with a full audit trail, and serves a daily USD
portfolio snapshot. Design and trade-offs are in **[SOLUTION.md](SOLUTION.md)**.

## Prerequisites

* .NET 8 SDK (pinned by `global.json`; any 8.0.3xx feature band)
* SQL Server. The defaults use **LocalDB** (`(localdb)\MSSQLLocalDB`, ships with Visual Studio). For any other
  instance, override the connection string (see below).
* PowerShell 7+ (`pwsh`) for the demo script.

## Run locally

```bash
dotnet tool restore                       # installs the pinned dotnet-ef tool
dotnet run --project src/TradeSnapshot.Api --launch-profile http
```

* API: `http://localhost:5080`. Swagger UI: `http://localhost:5080/swagger` (in `Development`, the root URL redirects there)
* In `Development` the API applies EF migrations on startup. That creates the schema, the stored procedure and the
  seed data (accounts `ACC-001`/`ACC-002`, AAPL/MSFT in USD, NPN in ZAR, prices for 2025-03-01, USD-ZAR 18.72).
* Other SQL Server: `ConnectionStrings__TradeSnapshot="Server=...;Database=TradeSnapshot;User Id=...;Password=...;TrustServerCertificate=True"`

### Create the schema without running the API

```bash
dotnet ef database update --project src/TradeSnapshot.Infrastructure --startup-project src/TradeSnapshot.Infrastructure
```

or run `sql/schema.sql` (idempotent script generated from the migrations) with `sqlcmd`/SSMS.

## Demo

```bash
pwsh ./scripts/demo.ps1
```

This drops and recreates the database, starts the API, then submits:

1. an initial trade (`CREATED`),
2. an identical re-send (`DUPLICATE`),
3. a correction with a later `as_of` (`CORRECTED`, version 2),
4. a late re-send of the original (`DUPLICATE`, no rollback),
5. an out-of-order older version (`STALE`),
6. a contradictory message with the same `as_of` (`409 CONFLICT`),
7. a ZAR trade.

It then prints the corrected snapshot (total **30,619.23 USD**) and the point-in-time snapshot before the
correction (**34,339.23 USD**), checks the expected totals, prints the audit trail and some JSON log lines, and
stops the API. `-UseRunningApi` runs the calls against an API you started yourself.
`scripts/demo.http` holds the same calls for manual use in VS / Rider / VS Code.

## Test

```bash
dotnet test
```

One end-to-end test (`tests/TradeSnapshot.IntegrationTests`) boots the API in-process against a **throwaway SQL
Server database** (LocalDB by default; set `TRADESNAPSHOT_TEST_SQL` to another server's connection string), runs the
full create, duplicate, correction, stale and conflict lifecycle, and asserts the snapshot values, the
point-in-time view and the audit trail.

## API

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/v1/trades` | Submit a trade event (201 created / 200 corrected, duplicate or stale / 409 conflict / 422 rejected / 400 invalid) |
| GET | `/api/v1/trades/{externalRef}` | Current version, version history, and every message received for it |
| GET | `/api/v1/accounts/{accountId}/snapshot?date=YYYY-MM-DD[&known_at=ISO-8601]` | USD snapshot: positions + totals |
| GET | `/api/v1/accounts/{accountId}/events?take=50` | Recent inbound messages and outcomes |
| GET | `/api/v1/instruments` | Reference instruments |
| PUT | `/api/v1/prices` | Upsert a close price `{ isin, date, price, currency }` |
| PUT | `/api/v1/fx-rates` | Upsert a rate `{ pair: "USD-ZAR", date, rate }` |
| GET | `/api/v1/admin/correction-handling?account=` | Effective rollout mode |
| GET | `/health/live`, `/health/ready` | Liveness / readiness (database) |

Trade payload (snake_case, as in the brief):

```json
{ "external_ref": "TRX-1001", "account_id": "ACC-001",
  "instrument": { "isin": "US0378331005", "symbol": "AAPL" },
  "side": "BUY", "quantity": 120, "price": 185.40,
  "trade_date": "2025-03-01", "as_of": "2025-03-01T10:15:00Z" }
```

## Rollout switch

```json
"TradeIngestion": { "CorrectionHandling": { "Mode": "Disabled | Shadow | Enabled", "PilotAccounts": ["ACC-001"] } }
```

Changes are picked up without a restart. See SOLUTION.md section 4.

## SQL (Task 2)

* `sql/usp_GetPortfolioSnapshot.sql`: the stored procedure (instrument summary and account summary result sets)
* `sql/snapshot_indexes.sql`: supporting index DDL
* `sql/examples.sql`: example calls, including a point-in-time `@KnownAtUtc` call

```sql
EXEC dbo.usp_GetPortfolioSnapshot @AccountCode = 'ACC-001', @ValuationDate = '2025-03-01';
EXEC dbo.usp_GetPortfolioSnapshot @AccountCode = 'ACC-001', @ValuationDate = '2025-03-01', @KnownAtUtc = '2025-03-01T12:00:00';
```
