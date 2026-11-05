using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeSnapshot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Deploys the Task 2 SQL artifact (sql/usp_GetPortfolioSnapshot.sql, embedded at build time) so the
    /// procedure the API calls is exactly the reviewed file. The script uses CREATE OR ALTER, so a later change
    /// ships as a new migration that re-applies the file.
    /// </summary>
    public partial class AddPortfolioSnapshotProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlScripts.Load("usp_GetPortfolioSnapshot.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetPortfolioSnapshot;");
        }
    }
}
