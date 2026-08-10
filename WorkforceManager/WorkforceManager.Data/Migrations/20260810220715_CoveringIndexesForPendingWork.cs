using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoveringIndexesForPendingWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductionScraps_ProductionStageId_Date",
                table: "ProductionScraps");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId",
                table: "DailyProductions");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionScraps_ProductionStageId_Date_PieceCount",
                table: "ProductionScraps",
                columns: new[] { "ProductionStageId", "Date", "PieceCount" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount",
                table: "DailyProductions",
                columns: new[] { "ProductionStageId", "Date", "IsDeleted", "PieceCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductionScraps_ProductionStageId_Date_PieceCount",
                table: "ProductionScraps");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount",
                table: "DailyProductions");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionScraps_ProductionStageId_Date",
                table: "ProductionScraps",
                columns: new[] { "ProductionStageId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId",
                table: "DailyProductions",
                column: "ProductionStageId");
        }
    }
}
