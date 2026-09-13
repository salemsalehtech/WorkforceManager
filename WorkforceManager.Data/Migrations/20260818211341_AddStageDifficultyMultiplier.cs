using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStageDifficultyMultiplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DifficultyMultiplier",
                table: "ProductionStages",
                type: "decimal(4,2)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionStage_Difficulty",
                table: "ProductionStages",
                sql: "[DifficultyMultiplier] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionStage_Difficulty",
                table: "ProductionStages");

            migrationBuilder.DropColumn(
                name: "DifficultyMultiplier",
                table: "ProductionStages");
        }
    }
}
