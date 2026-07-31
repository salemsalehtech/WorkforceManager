using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductionBatchId",
                table: "DailyProductions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductionBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCompletedStageId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SplitFromBatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionBatches_SplitFromBatchId",
                        column: x => x.SplitFromBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionStages_LastCompletedStageId",
                        column: x => x.LastCompletedStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionDayClosures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CarriedBatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CarriedPieces = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDayClosures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionBatchId",
                table: "DailyProductions",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_CompletedDate",
                table: "ProductionBatches",
                column: "CompletedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_LastCompletedStageId",
                table: "ProductionBatches",
                column: "LastCompletedStageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductId_Status",
                table: "ProductionBatches",
                columns: new[] { "ProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_SplitFromBatchId",
                table: "ProductionBatches",
                column: "SplitFromBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_StartedDate",
                table: "ProductionBatches",
                column: "StartedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDayClosures_Date",
                table: "ProductionDayClosures",
                column: "Date",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyProductions_ProductionBatches_ProductionBatchId",
                table: "DailyProductions",
                column: "ProductionBatchId",
                principalTable: "ProductionBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyProductions_ProductionBatches_ProductionBatchId",
                table: "DailyProductions");

            migrationBuilder.DropTable(
                name: "ProductionBatches");

            migrationBuilder.DropTable(
                name: "ProductionDayClosures");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionBatchId",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "ProductionBatchId",
                table: "DailyProductions");
        }
    }
}
