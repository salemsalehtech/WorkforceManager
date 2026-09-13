using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionScrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScrapReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionScraps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PieceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ScrapReasonId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionScraps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionScraps_ProductionStages_ProductionStageId",
                        column: x => x.ProductionStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionScraps_ScrapReasons_ScrapReasonId",
                        column: x => x.ScrapReasonId,
                        principalTable: "ScrapReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionScraps_Date",
                table: "ProductionScraps",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionScraps_ProductionStageId_Date",
                table: "ProductionScraps",
                columns: new[] { "ProductionStageId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionScraps_ScrapReasonId",
                table: "ProductionScraps",
                column: "ScrapReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapReasons_Name",
                table: "ScrapReasons",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionScraps");

            migrationBuilder.DropTable(
                name: "ScrapReasons");
        }
    }
}
