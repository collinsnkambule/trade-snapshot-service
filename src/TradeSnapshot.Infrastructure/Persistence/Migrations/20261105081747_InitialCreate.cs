using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TradeSnapshot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                schema: "dbo",
                columns: table => new
                {
                    BaseCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    RateDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => new { x.BaseCurrency, x.QuoteCurrency, x.RateDate });
                    table.CheckConstraint("CK_FxRates_Rate", "[Rate] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Isin = table.Column<string>(type: "char(12)", unicode: false, fixedLength: true, maxLength: 12, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstrumentPrices",
                schema: "dbo",
                columns: table => new
                {
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosePrice = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstrumentPrices", x => new { x.InstrumentId, x.PriceDate });
                    table.CheckConstraint("CK_InstrumentPrices_ClosePrice", "[ClosePrice] >= 0");
                    table.ForeignKey(
                        name: "FK_InstrumentPrices_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalSchema: "dbo",
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    LatestAsOfUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "dbo",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalSchema: "dbo",
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeIngestionLog",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AccountCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AsOfUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ShadowOutcome = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    TradeId = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: true),
                    Fingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeIngestionLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeIngestionLog_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "dbo",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeVersions",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<byte>(type: "tinyint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AsOfUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Fingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeVersions", x => x.Id);
                    table.CheckConstraint("CK_TradeVersions_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_TradeVersions_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_TradeVersions_Side", "[Side] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_TradeVersions_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "dbo",
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Accounts",
                columns: new[] { "Id", "AccountCode", "BaseCurrency", "Name" },
                values: new object[,]
                {
                    { 1, "ACC-001", "USD", "Demo Growth Portfolio" },
                    { 2, "ACC-002", "USD", "Demo Income Portfolio" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "FxRates",
                columns: new[] { "BaseCurrency", "QuoteCurrency", "RateDate", "Rate", "UpdatedAtUtc" },
                values: new object[] { "USD", "ZAR", new DateOnly(2025, 3, 1), 18.72m, new DateTime(2025, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Instruments",
                columns: new[] { "Id", "Currency", "Isin", "Name", "Symbol" },
                values: new object[,]
                {
                    { 1, "USD", "US0378331005", "Apple Inc.", "AAPL" },
                    { 2, "USD", "US5949181045", "Microsoft Corp.", "MSFT" },
                    { 3, "ZAR", "ZAE000015889", "Naspers Ltd (JSE)", "NPN" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "InstrumentPrices",
                columns: new[] { "InstrumentId", "PriceDate", "ClosePrice", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, new DateOnly(2025, 3, 1), 186.00m, new DateTime(2025, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 1, new DateOnly(2025, 3, 3), 188.50m, new DateTime(2025, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, new DateOnly(2025, 3, 1), 397.00m, new DateTime(2025, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, new DateOnly(2025, 3, 1), 4500.00m, new DateTime(2025, 3, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "UX_Accounts_AccountCode",
                schema: "dbo",
                table: "Accounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Symbol",
                schema: "dbo",
                table: "Instruments",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "UX_Instruments_Isin",
                schema: "dbo",
                table: "Instruments",
                column: "Isin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeIngestionLog_Account_ReceivedAt",
                schema: "dbo",
                table: "TradeIngestionLog",
                columns: new[] { "AccountCode", "ReceivedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TradeIngestionLog_ExternalRef",
                schema: "dbo",
                table: "TradeIngestionLog",
                column: "ExternalRef");

            migrationBuilder.CreateIndex(
                name: "IX_TradeIngestionLog_TradeId",
                schema: "dbo",
                table: "TradeIngestionLog",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_AccountId",
                schema: "dbo",
                table: "Trades",
                column: "AccountId")
                .Annotation("SqlServer:Include", new[] { "InstrumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_InstrumentId",
                schema: "dbo",
                table: "Trades",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "UX_Trades_ExternalRef",
                schema: "dbo",
                table: "Trades",
                column: "ExternalRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TradeVersions_TradeId_AsOfUtc",
                schema: "dbo",
                table: "TradeVersions",
                columns: new[] { "TradeId", "AsOfUtc" },
                unique: true,
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Side", "Quantity", "Price", "TradeDate" });

            migrationBuilder.CreateIndex(
                name: "UX_TradeVersions_TradeId_Version",
                schema: "dbo",
                table: "TradeVersions",
                columns: new[] { "TradeId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FxRates",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "InstrumentPrices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TradeIngestionLog",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TradeVersions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Trades",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Instruments",
                schema: "dbo");
        }
    }
}
