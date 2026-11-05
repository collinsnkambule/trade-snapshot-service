/* =====================================================================================================
   Supporting indexes for dbo.usp_GetPortfolioSnapshot.

   These are created by the EF Core migration (see TradeConfigurations.cs); this script documents them as DDL
   and is idempotent, so it is safe to run against a database built from sql/schema.sql or by migrations.

   Access paths used by the procedure:
     1. Accounts      : AccountCode -> Id                      UX_Accounts_AccountCode (unique seek)
     2. Trades        : AccountId -> (Id, InstrumentId)         IX_Trades_AccountId     (seek, covering)
     3. TradeVersions : TradeId, AsOfUtc DESC <= @Cutoff        UX_TradeVersions_TradeId_AsOfUtc (seek, covering,
                        ordered for ROW_NUMBER() PARTITION BY TradeId ORDER BY AsOfUtc DESC: no sort operator)
     4. InstrumentPrices: InstrumentId, PriceDate <= @Date, TOP 1 DESC   PK_InstrumentPrices (clustered; existing key suffices)
     5. FxRates       : USD, QuoteCurrency, RateDate <= @Date, TOP 1 DESC PK_FxRates (clustered; existing key suffices)
   ===================================================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Accounts_AccountCode' AND object_id = OBJECT_ID(N'dbo.Accounts'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_Accounts_AccountCode
        ON dbo.Accounts (AccountCode);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Trades_AccountId' AND object_id = OBJECT_ID(N'dbo.Trades'))
    CREATE NONCLUSTERED INDEX IX_Trades_AccountId
        ON dbo.Trades (AccountId)
        INCLUDE (InstrumentId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_TradeVersions_TradeId_AsOfUtc' AND object_id = OBJECT_ID(N'dbo.TradeVersions'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_TradeVersions_TradeId_AsOfUtc
        ON dbo.TradeVersions (TradeId ASC, AsOfUtc DESC)
        INCLUDE (Side, Quantity, Price, TradeDate);

-- PK_InstrumentPrices (InstrumentId, PriceDate) and PK_FxRates (BaseCurrency, QuoteCurrency, RateDate) are
-- clustered primary keys whose leading columns match the equality predicates and whose last column matches the
-- range + ORDER BY ... DESC, so the TOP (1) as-of lookups are single-row backward seeks. No extra index needed.

/* Higher-volume options (not created here; see SOLUTION.md):
   -- Filtered index if most trades are terminal/archived and only "live" ones are snapshotted
   -- Partition TradeVersions on a monthly AsOfUtc scheme aligned with archival (sliding window)
   -- Columnstore on an archive/reporting replica for firm-wide aggregation
*/
