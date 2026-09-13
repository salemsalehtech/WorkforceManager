using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseCleanupAndIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_EventType",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ProductionDayClosures");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "HourlyWorkLogs");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "CheckInTime",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckOutTime",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Attendances");

            migrationBuilder.AlterColumn<decimal>(
                name: "MeasuredRatio",
                table: "WorkerSkills",
                type: "decimal(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkerSkill_Stars",
                table: "WorkerSkills",
                sql: "[Stars] BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Worker_DailyWage",
                table: "Workers",
                sql: "[DailyWageEgp] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WageAdjustment_Amount",
                table: "WageAdjustments",
                sql: "[AmountEgp] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionStage_Quota",
                table: "ProductionStages",
                sql: "[PiecesPerWorkday] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyProduction_Amounts",
                table: "DailyProductions",
                sql: "[PieceCount] >= 0 AND [PiecesPerWorkdayAtEntry] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkerSkill_Stars",
                table: "WorkerSkills");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Worker_DailyWage",
                table: "Workers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WageAdjustment_Amount",
                table: "WageAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionStage_Quota",
                table: "ProductionStages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyProduction_Amounts",
                table: "DailyProductions");

            migrationBuilder.AlterColumn<decimal>(
                name: "MeasuredRatio",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ProductionDayClosures",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Penalties",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "HourlyWorkLogs",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DailyProductions",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CheckInTime",
                table: "Attendances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "CheckOutTime",
                table: "Attendances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Attendances",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_EventType",
                table: "ActivityEvents",
                column: "EventType");
        }
    }
}
