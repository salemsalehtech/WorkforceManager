using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyPlanSubPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonthlyPlans_ProductId_Year_Month",
                table: "MonthlyPlans");

            migrationBuilder.AddColumn<int>(
                name: "SubPeriodId",
                table: "MonthlyPlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MonthlyPlanSubPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyPlanSubPeriods", x => x.Id);
                    table.CheckConstraint("CK_MonthlyPlanSubPeriod_EndDate_NotBeforeStart", "EndDate >= StartDate");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlans_ProductId_Year_Month_SubPeriodId",
                table: "MonthlyPlans",
                columns: new[] { "ProductId", "Year", "Month", "SubPeriodId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyPlanSubPeriods");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyPlans_ProductId_Year_Month_SubPeriodId",
                table: "MonthlyPlans");

            migrationBuilder.DropColumn(
                name: "SubPeriodId",
                table: "MonthlyPlans");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlans_ProductId_Year_Month",
                table: "MonthlyPlans",
                columns: new[] { "ProductId", "Year", "Month" },
                unique: true);
        }
    }
}
