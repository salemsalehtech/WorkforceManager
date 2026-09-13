using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Penalties_Date",
                table: "Penalties",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_Date",
                table: "DailyProductions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_Date",
                table: "Attendances",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Penalties_Date",
                table: "Penalties");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_Date",
                table: "DailyProductions");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_Date",
                table: "Attendances");
        }
    }
}
