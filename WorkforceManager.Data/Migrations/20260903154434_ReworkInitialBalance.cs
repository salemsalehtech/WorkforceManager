using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReworkInitialBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "WorkerId",
                table: "InitialBalanceUsages",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "DailyProductionId",
                table: "InitialBalanceUsages",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ProductionScrapId",
                table: "InitialBalanceUsages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_ProductionScrapId",
                table: "InitialBalanceUsages",
                column: "ProductionScrapId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InitialBalanceUsages_ProductionScraps_ProductionScrapId",
                table: "InitialBalanceUsages",
                column: "ProductionScrapId",
                principalTable: "ProductionScraps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InitialBalanceUsages_ProductionScraps_ProductionScrapId",
                table: "InitialBalanceUsages");

            migrationBuilder.DropIndex(
                name: "IX_InitialBalanceUsages_ProductionScrapId",
                table: "InitialBalanceUsages");

            migrationBuilder.DropColumn(
                name: "ProductionScrapId",
                table: "InitialBalanceUsages");

            migrationBuilder.AlterColumn<int>(
                name: "WorkerId",
                table: "InitialBalanceUsages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DailyProductionId",
                table: "InitialBalanceUsages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
