IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Accounts] (
        [Id] int NOT NULL IDENTITY,
        [AccountCode] varchar(32) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [BaseCurrency] char(3) NOT NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[FxRates] (
        [BaseCurrency] char(3) NOT NULL,
        [QuoteCurrency] char(3) NOT NULL,
        [RateDate] date NOT NULL,
        [Rate] decimal(19,8) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_FxRates] PRIMARY KEY ([BaseCurrency], [QuoteCurrency], [RateDate]),
        CONSTRAINT [CK_FxRates_Rate] CHECK ([Rate] > 0)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Instruments] (
        [Id] int NOT NULL IDENTITY,
        [Isin] char(12) NOT NULL,
        [Symbol] varchar(16) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Currency] char(3) NOT NULL,
        CONSTRAINT [PK_Instruments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[InstrumentPrices] (
        [InstrumentId] int NOT NULL,
        [PriceDate] date NOT NULL,
        [ClosePrice] decimal(19,6) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        CONSTRAINT [PK_InstrumentPrices] PRIMARY KEY ([InstrumentId], [PriceDate]),
        CONSTRAINT [CK_InstrumentPrices_ClosePrice] CHECK ([ClosePrice] >= 0),
        CONSTRAINT [FK_InstrumentPrices_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [dbo].[Instruments] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Trades] (
        [Id] bigint NOT NULL IDENTITY,
        [ExternalRef] varchar(64) NOT NULL,
        [AccountId] int NOT NULL,
        [InstrumentId] int NOT NULL,
        [CurrentVersion] int NOT NULL,
        [LatestAsOfUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Trades] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Trades_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Trades_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [dbo].[Instruments] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[TradeIngestionLog] (
        [Id] bigint NOT NULL IDENTITY,
        [ExternalRef] varchar(64) NOT NULL,
        [AccountCode] varchar(32) NOT NULL,
        [AsOfUtc] datetime2(7) NOT NULL,
        [ReceivedAtUtc] datetime2(7) NOT NULL,
        [Outcome] varchar(16) NOT NULL,
        [Reason] varchar(64) NOT NULL,
        [Mode] varchar(16) NOT NULL,
        [ShadowOutcome] varchar(16) NULL,
        [TradeId] bigint NULL,
        [Version] int NULL,
        [Fingerprint] binary(32) NULL,
        [CorrelationId] varchar(64) NULL,
        CONSTRAINT [PK_TradeIngestionLog] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TradeIngestionLog_Trades_TradeId] FOREIGN KEY ([TradeId]) REFERENCES [dbo].[Trades] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[TradeVersions] (
        [Id] bigint NOT NULL IDENTITY,
        [TradeId] bigint NOT NULL,
        [Version] int NOT NULL,
        [Side] tinyint NOT NULL,
        [Quantity] decimal(19,6) NOT NULL,
        [Price] decimal(19,6) NOT NULL,
        [TradeDate] date NOT NULL,
        [AsOfUtc] datetime2(7) NOT NULL,
        [ReceivedAtUtc] datetime2(7) NOT NULL,
        [Fingerprint] binary(32) NOT NULL,
        [CorrelationId] varchar(64) NULL,
        CONSTRAINT [PK_TradeVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TradeVersions_Price] CHECK ([Price] >= 0),
        CONSTRAINT [CK_TradeVersions_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_TradeVersions_Side] CHECK ([Side] IN (1, 2)),
        CONSTRAINT [FK_TradeVersions_Trades_TradeId] FOREIGN KEY ([TradeId]) REFERENCES [dbo].[Trades] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AccountCode', N'BaseCurrency', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[Accounts]'))
        SET IDENTITY_INSERT [dbo].[Accounts] ON;
    EXEC(N'INSERT INTO [dbo].[Accounts] ([Id], [AccountCode], [BaseCurrency], [Name])
    VALUES (1, ''ACC-001'', ''USD'', N''Demo Growth Portfolio''),
    (2, ''ACC-002'', ''USD'', N''Demo Income Portfolio'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AccountCode', N'BaseCurrency', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[Accounts]'))
        SET IDENTITY_INSERT [dbo].[Accounts] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'BaseCurrency', N'QuoteCurrency', N'RateDate', N'Rate', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[FxRates]'))
        SET IDENTITY_INSERT [dbo].[FxRates] ON;
    EXEC(N'INSERT INTO [dbo].[FxRates] ([BaseCurrency], [QuoteCurrency], [RateDate], [Rate], [UpdatedAtUtc])
    VALUES (''USD'', ''ZAR'', ''2025-03-01'', 18.72, ''2025-03-01T00:00:00.0000000Z'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'BaseCurrency', N'QuoteCurrency', N'RateDate', N'Rate', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[FxRates]'))
        SET IDENTITY_INSERT [dbo].[FxRates] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Currency', N'Isin', N'Name', N'Symbol') AND [object_id] = OBJECT_ID(N'[dbo].[Instruments]'))
        SET IDENTITY_INSERT [dbo].[Instruments] ON;
    EXEC(N'INSERT INTO [dbo].[Instruments] ([Id], [Currency], [Isin], [Name], [Symbol])
    VALUES (1, ''USD'', ''US0378331005'', N''Apple Inc.'', ''AAPL''),
    (2, ''USD'', ''US5949181045'', N''Microsoft Corp.'', ''MSFT''),
    (3, ''ZAR'', ''ZAE000015889'', N''Naspers Ltd (JSE)'', ''NPN'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Currency', N'Isin', N'Name', N'Symbol') AND [object_id] = OBJECT_ID(N'[dbo].[Instruments]'))
        SET IDENTITY_INSERT [dbo].[Instruments] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'InstrumentId', N'PriceDate', N'ClosePrice', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[InstrumentPrices]'))
        SET IDENTITY_INSERT [dbo].[InstrumentPrices] ON;
    EXEC(N'INSERT INTO [dbo].[InstrumentPrices] ([InstrumentId], [PriceDate], [ClosePrice], [UpdatedAtUtc])
    VALUES (1, ''2025-03-01'', 186.0, ''2025-03-01T00:00:00.0000000Z''),
    (1, ''2025-03-03'', 188.5, ''2025-03-01T00:00:00.0000000Z''),
    (2, ''2025-03-01'', 397.0, ''2025-03-01T00:00:00.0000000Z''),
    (3, ''2025-03-01'', 4500.0, ''2025-03-01T00:00:00.0000000Z'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'InstrumentId', N'PriceDate', N'ClosePrice', N'UpdatedAtUtc') AND [object_id] = OBJECT_ID(N'[dbo].[InstrumentPrices]'))
        SET IDENTITY_INSERT [dbo].[InstrumentPrices] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Accounts_AccountCode] ON [dbo].[Accounts] ([AccountCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Instruments_Symbol] ON [dbo].[Instruments] ([Symbol]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Instruments_Isin] ON [dbo].[Instruments] ([Isin]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TradeIngestionLog_Account_ReceivedAt] ON [dbo].[TradeIngestionLog] ([AccountCode], [ReceivedAtUtc] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TradeIngestionLog_ExternalRef] ON [dbo].[TradeIngestionLog] ([ExternalRef]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TradeIngestionLog_TradeId] ON [dbo].[TradeIngestionLog] ([TradeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_AccountId] ON [dbo].[Trades] ([AccountId]) INCLUDE ([InstrumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trades_InstrumentId] ON [dbo].[Trades] ([InstrumentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Trades_ExternalRef] ON [dbo].[Trades] ([ExternalRef]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TradeVersions_TradeId_AsOfUtc] ON [dbo].[TradeVersions] ([TradeId], [AsOfUtc] DESC) INCLUDE ([Side], [Quantity], [Price], [TradeDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_TradeVersions_TradeId_Version] ON [dbo].[TradeVersions] ([TradeId], [Version]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081747_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105081747_InitialCreate', N'8.0.33');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081754_AddPortfolioSnapshotProcedure'
)
BEGIN
    /* =====================================================================================================
       dbo.usp_GetPortfolioSnapshot  (Task 2 SQL artifact; also deployed by the EF migration and called by the API)

       Returns, for one account and valuation date, in USD:
         Result set 1 - instrument-level summary: quantity, unit cost (average cost), price, market value,
                        cost basis and unrealised P/L.
         Result set 2 - account-level summary: totals, position counts and data-quality counters.

       Parameters
         @AccountCode    Upstream account identifier (e.g. 'ACC-001').
         @ValuationDate  Business date. Trades with TradeDate <= @ValuationDate are included. Prices and FX use the
                         latest row ON OR BEFORE this date (carry-forward over weekends/holidays).
         @KnownAtUtc     Optional knowledge cutoff (bitemporal "as known at"). For each trade only the latest version
                         with AsOfUtc <= @KnownAtUtc is used, so a correction asserted later does not leak into a
                         report re-run for an earlier point in time. NULL = latest known version (corrected view).

       Correctness rules handled at the data-model level
         * Corrections are append-only TradeVersions rows; "latest version per trade within the cutoff" is chosen
           with ROW_NUMBER() over the covering index UX_TradeVersions_TradeId_AsOfUtc.
         * Version selection happens BEFORE the TradeDate filter, because a correction may move the trade date.
         * Re-sent duplicates never create rows, so they can never be double-counted here.

       Unit cost: weighted average cost of the effective buys (long) / sells (short) for the instrument, in the
       instrument currency. See SOLUTION.md for why this simplification was chosen and what production would do.

       Precision: intermediates keep >= 10dp (operands are cast explicitly so SQL Server's decimal division rules
       do not truncate scale to 6); money is rounded to 2dp (half away from zero) only in
       the final projection, and totals are summed from unrounded values, so totals never drift from rounding.
       ===================================================================================================== */
    CREATE OR ALTER PROCEDURE dbo.usp_GetPortfolioSnapshot
        @AccountCode   varchar(32),
        @ValuationDate date,
        @KnownAtUtc    datetime2(7) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        -- Replace the optional parameter with a concrete upper bound so the predicate below stays a plain,
        -- SARGable range (no "@p IS NULL OR col <= @p" catch-all that defeats index seeks).
        DECLARE @Cutoff datetime2(7) = ISNULL(@KnownAtUtc, CONVERT(datetime2(7), '9999-12-31T23:59:59.9999999'));

        DECLARE @AccountId int;
        SELECT @AccountId = a.Id FROM dbo.Accounts AS a WHERE a.AccountCode = @AccountCode;

        CREATE TABLE #Positions
        (
            InstrumentId      int            NOT NULL PRIMARY KEY,
            Isin              char(12)       NOT NULL,
            Symbol            varchar(16)    NOT NULL,
            Currency          char(3)        NOT NULL,
            TradeCount        int            NOT NULL,
            NetQuantity       decimal(28,10) NOT NULL,
            UnitCostLocal     decimal(28,10) NULL,
            PriceLocal        decimal(19,6)  NULL,
            PriceDate         date           NULL,
            UsdFxRate         decimal(19,8)  NULL,   -- units of instrument currency per 1 USD (1 for USD)
            FxRateDate        date           NULL,
            MarketValueLocal  decimal(38,10) NULL,
            MarketValueUsd    decimal(38,10) NULL,
            CostBasisUsd      decimal(38,10) NULL,
            UnrealizedPnlUsd  decimal(38,10) NULL
        );

        WITH RankedVersions AS
        (
            SELECT  t.InstrumentId,
                    v.Side,
                    v.Quantity,
                    v.Price,
                    v.TradeDate,
                    ROW_NUMBER() OVER (PARTITION BY v.TradeId ORDER BY v.AsOfUtc DESC) AS VersionRank
            FROM    dbo.Trades        AS t
            JOIN    dbo.TradeVersions AS v ON v.TradeId = t.Id
            WHERE   t.AccountId = @AccountId
              AND   v.AsOfUtc  <= @Cutoff
        ),
        EffectiveTrades AS
        (
            SELECT  InstrumentId, Side, Quantity, Price
            FROM    RankedVersions
            WHERE   VersionRank = 1
              AND   TradeDate  <= @ValuationDate
        ),
        Holdings AS
        (
            SELECT  InstrumentId,
                    COUNT(*)                                                               AS TradeCount,
                    SUM(CASE WHEN Side = 1 THEN Quantity ELSE -Quantity END)               AS NetQuantity,
                    SUM(CASE WHEN Side = 1 THEN Quantity END)                              AS BuyQuantity,
                    SUM(CASE WHEN Side = 1 THEN CAST(Quantity * Price AS decimal(38,10)) END) AS BuyCost,
                    SUM(CASE WHEN Side = 2 THEN Quantity END)                              AS SellQuantity,
                    SUM(CASE WHEN Side = 2 THEN CAST(Quantity * Price AS decimal(38,10)) END) AS SellProceeds
            FROM    EffectiveTrades
            GROUP BY InstrumentId
        ),
        Costed AS
        (
            SELECT  h.InstrumentId,
                    h.TradeCount,
                    CAST(h.NetQuantity AS decimal(28,10)) AS NetQuantity,
                    -- Operands are narrowed before dividing: SQL Server cuts a quotient's scale to 6 when the
                    -- operand precisions are large, so decimal(28,10)/decimal(19,6) keeps 14dp instead.
                    CAST(CASE
                            WHEN h.NetQuantity > 0 THEN CAST(h.BuyCost      AS decimal(28,10)) / NULLIF(CAST(h.BuyQuantity  AS decimal(19,6)), 0)
                            WHEN h.NetQuantity < 0 THEN CAST(h.SellProceeds AS decimal(28,10)) / NULLIF(CAST(h.SellQuantity AS decimal(19,6)), 0)
                         END AS decimal(28,10))       AS UnitCostLocal
            FROM    Holdings AS h
            WHERE   h.NetQuantity <> 0                 -- flat positions are not reported
        )
        INSERT #Positions (InstrumentId, Isin, Symbol, Currency, TradeCount, NetQuantity, UnitCostLocal,
                           PriceLocal, PriceDate, UsdFxRate, FxRateDate)
        SELECT  c.InstrumentId, i.Isin, i.Symbol, i.Currency, c.TradeCount, c.NetQuantity, c.UnitCostLocal,
                px.ClosePrice, px.PriceDate,
                CASE WHEN i.Currency = 'USD' THEN CAST(1 AS decimal(19,8)) ELSE fx.Rate END,
                CASE WHEN i.Currency = 'USD' THEN @ValuationDate          ELSE fx.RateDate END
        FROM    Costed AS c
        JOIN    dbo.Instruments AS i ON i.Id = c.InstrumentId
        OUTER APPLY (   -- as-of join: latest price on or before the valuation date (seek on PK_InstrumentPrices)
                        SELECT TOP (1) p.ClosePrice, p.PriceDate
                        FROM   dbo.InstrumentPrices AS p
                        WHERE  p.InstrumentId = c.InstrumentId
                          AND  p.PriceDate   <= @ValuationDate
                        ORDER BY p.PriceDate DESC
                    ) AS px
        OUTER APPLY (   -- as-of join: latest USD->ccy rate on or before the valuation date (seek on PK_FxRates)
                        SELECT TOP (1) f.Rate, f.RateDate
                        FROM   dbo.FxRates AS f
                        WHERE  f.BaseCurrency  = 'USD'
                          AND  f.QuoteCurrency = i.Currency
                          AND  f.RateDate     <= @ValuationDate
                          AND  i.Currency     <> 'USD'
                        ORDER BY f.RateDate DESC
                    ) AS fx;

        -- Valuation in one pass over the (small) per-account working set. NULL propagates when a price or FX rate
        -- is missing, so an unpriced position is visible rather than silently valued at zero.
        -- Local amounts are materialised as decimal(28,10) before dividing by the rate, for the same
        -- scale-preservation reason as above (gives a 12dp quotient).
        UPDATE p
        SET    MarketValueLocal = v.MarketValueLocal,
               MarketValueUsd   = v.MarketValueLocal / p.UsdFxRate,
               CostBasisUsd     = v.CostBasisLocal   / p.UsdFxRate,
               UnrealizedPnlUsd = (v.MarketValueLocal - v.CostBasisLocal) / p.UsdFxRate
        FROM   #Positions AS p
        CROSS APPLY (SELECT CAST(CAST(p.NetQuantity AS decimal(19,6)) * p.PriceLocal                         AS decimal(28,10)) AS MarketValueLocal,
                            CAST(CAST(p.NetQuantity AS decimal(19,6)) * CAST(p.UnitCostLocal AS decimal(19,10)) AS decimal(28,10)) AS CostBasisLocal) AS v;

        /* ---- Result set 1: instrument-level summary ---- */
        SELECT  p.Isin,
                p.Symbol,
                p.Currency,
                p.TradeCount,
                CAST(p.NetQuantity      AS decimal(19,6))               AS Quantity,
                CAST(ROUND(p.UnitCostLocal, 6) AS decimal(19,6))        AS UnitCostLocal,
                p.PriceLocal,
                p.PriceDate,
                p.UsdFxRate,
                p.FxRateDate,
                CAST(ROUND(p.MarketValueLocal, 2) AS decimal(19,2))     AS MarketValueLocal,
                CAST(ROUND(p.MarketValueUsd,   2) AS decimal(19,2))     AS MarketValueUsd,
                CAST(ROUND(p.CostBasisUsd,     2) AS decimal(19,2))     AS CostBasisUsd,
                CAST(ROUND(p.UnrealizedPnlUsd, 2) AS decimal(19,2))     AS UnrealizedPnlUsd,
                CAST(CASE
                        WHEN p.PriceLocal IS NULL           THEN 'MISSING_PRICE'
                        WHEN p.UsdFxRate  IS NULL           THEN 'MISSING_FX_RATE'
                        WHEN p.PriceDate  < @ValuationDate  THEN 'STALE_PRICE'
                        WHEN p.FxRateDate < @ValuationDate  THEN 'STALE_FX_RATE'
                        ELSE 'OK'
                     END AS varchar(16))                                AS ValuationStatus
        FROM    #Positions AS p
        ORDER BY p.Symbol;

        /* ---- Result set 2: account-level summary (from the same working set: no second pass over trades) ---- */
        SELECT  @AccountCode                                                         AS AccountCode,
                @ValuationDate                                                       AS ValuationDate,
                @KnownAtUtc                                                          AS KnownAtUtc,
                CAST('USD' AS char(3))                                               AS BaseCurrency,
                COUNT(*)                                                             AS PositionCount,
                ISNULL(SUM(CASE WHEN p.MarketValueUsd IS NULL THEN 1 ELSE 0 END), 0) AS UnpricedPositionCount,
                CAST(ROUND(ISNULL(SUM(p.MarketValueUsd),   0), 2) AS decimal(19,2))  AS TotalMarketValueUsd,
                CAST(ROUND(ISNULL(SUM(p.CostBasisUsd),     0), 2) AS decimal(19,2))  AS TotalCostBasisUsd,
                CAST(ROUND(ISNULL(SUM(p.UnrealizedPnlUsd), 0), 2) AS decimal(19,2))  AS TotalUnrealizedPnlUsd,
                CAST(0 AS decimal(19,2))                                             AS CashUsd,  -- cash not modelled (see SOLUTION.md)
                CAST(ROUND(ISNULL(SUM(p.MarketValueUsd),   0), 2) AS decimal(19,2))  AS TotalValueUsd
        FROM    #Positions AS p;
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105081754_AddPortfolioSnapshotProcedure'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105081754_AddPortfolioSnapshotProcedure', N'8.0.33');
END;
GO

COMMIT;
GO

