using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyPlanCorrectionAndWorkCalendarHoliday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyPlanCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyPlanCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyPlanCorrections_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyWorkCalendarHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyWorkCalendarHolidays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlanCorrections_ProductId_Date",
                table: "MonthlyPlanCorrections",
                columns: new[] { "ProductId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyWorkCalendarHolidays_Date",
                table: "MonthlyWorkCalendarHolidays",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyPlanCorrections");

            migrationBuilder.DropTable(
                name: "MonthlyWorkCalendarHolidays");
        }
    }
}
