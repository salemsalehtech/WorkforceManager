using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPeriodAndTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WorkdayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPeriods", x => x.Id);
                    table.CheckConstraint("CK_PlanPeriod_EndDate_NotBeforeStart", "EndDate >= StartDate");
                    table.CheckConstraint("CK_PlanPeriod_WorkdayCount_NonNegative", "WorkdayCount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PlanPeriodTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanPeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedQuantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPeriodTargets", x => x.Id);
                    table.CheckConstraint("CK_PlanPeriodTarget_PlannedQuantity_NonNegative", "PlannedQuantity >= 0");
                    table.ForeignKey(
                        name: "FK_PlanPeriodTargets_PlanPeriods_PlanPeriodId",
                        column: x => x.PlanPeriodId,
                        principalTable: "PlanPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanPeriodTargets_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanPeriodTargets_PlanPeriodId_ProductId",
                table: "PlanPeriodTargets",
                columns: new[] { "PlanPeriodId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanPeriodTargets_ProductId",
                table: "PlanPeriodTargets",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanPeriodTargets");

            migrationBuilder.DropTable(
                name: "PlanPeriods");
        }
    }
}
