using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapAndStageOutputPieceCountChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionStageOutput_PieceCount",
                table: "ProductionStageOutputs",
                sql: "[PieceCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionScrap_PieceCount",
                table: "ProductionScraps",
                sql: "[PieceCount] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionStageOutput_PieceCount",
                table: "ProductionStageOutputs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionScrap_PieceCount",
                table: "ProductionScraps");
        }
    }
}
