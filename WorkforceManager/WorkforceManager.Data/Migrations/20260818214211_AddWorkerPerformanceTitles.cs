using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerPerformanceTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerPerformanceTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TitleType = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerPerformanceTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerPerformanceTitles_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerPerformanceTitles_TitleType_PeriodStart",
                table: "WorkerPerformanceTitles",
                columns: new[] { "TitleType", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerPerformanceTitles_WorkerId",
                table: "WorkerPerformanceTitles",
                column: "WorkerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerPerformanceTitles");
        }
    }
}
