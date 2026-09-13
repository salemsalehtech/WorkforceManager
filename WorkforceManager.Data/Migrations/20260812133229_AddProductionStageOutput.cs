using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionStageOutput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionStageOutputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PieceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionStageOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionStageOutputs_ProductionStages_ProductionStageId",
                        column: x => x.ProductionStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionStageOutputs_Date",
                table: "ProductionStageOutputs",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionStageOutputs_ProductionStageId_Date",
                table: "ProductionStageOutputs",
                columns: new[] { "ProductionStageId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionStageOutputs_ProductionStageId_Date_PieceCount",
                table: "ProductionStageOutputs",
                columns: new[] { "ProductionStageId", "Date", "PieceCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionStageOutputs");
        }
    }
}
