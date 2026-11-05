/* Example calls for dbo.usp_GetPortfolioSnapshot. Run after the demo (scripts/demo.ps1) has loaded trades. */

-- Corrected view: latest known version of every trade, valued on 2025-03-01.
EXEC dbo.usp_GetPortfolioSnapshot @AccountCode = 'ACC-001', @ValuationDate = '2025-03-01';

-- Point-in-time view: what we knew at 12:00 UTC, before the 14:00 correction arrived.
EXEC dbo.usp_GetPortfolioSnapshot @AccountCode = 'ACC-001', @ValuationDate = '2025-03-01', @KnownAtUtc = '2025-03-01T12:00:00';

-- Price carry-forward: 2025-03-02 has no price rows, so 2025-03-01 prices are used and flagged STALE_PRICE.
EXEC dbo.usp_GetPortfolioSnapshot @AccountCode = 'ACC-001', @ValuationDate = '2025-03-02';

-- Audit trail for one trade: every version, newest first.
SELECT t.ExternalRef, v.Version, v.Side, v.Quantity, v.Price, v.TradeDate, v.AsOfUtc, v.ReceivedAtUtc
FROM   dbo.Trades t JOIN dbo.TradeVersions v ON v.TradeId = t.Id
WHERE  t.ExternalRef = 'TRX-1001'
ORDER BY v.Version DESC;

-- Every inbound message and what we decided (duplicates and rejections included).
SELECT ExternalRef, AsOfUtc, ReceivedAtUtc, Mode, Outcome, ShadowOutcome, Reason, Version, CorrelationId
FROM   dbo.TradeIngestionLog
WHERE  AccountCode = 'ACC-001'
ORDER BY ReceivedAtUtc DESC;
