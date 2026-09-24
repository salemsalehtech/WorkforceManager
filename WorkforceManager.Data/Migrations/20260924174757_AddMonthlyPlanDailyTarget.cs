using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyPlanDailyTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyTargetQuantity",
                table: "MonthlyPlans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonthlyPlan_DailyTargetQuantity_NonNegative",
                table: "MonthlyPlans",
                sql: "DailyTargetQuantity IS NULL OR DailyTargetQuantity >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MonthlyPlan_DailyTargetQuantity_NonNegative",
                table: "MonthlyPlans");

            migrationBuilder.DropColumn(
                name: "DailyTargetQuantity",
                table: "MonthlyPlans");
        }
    }
}
