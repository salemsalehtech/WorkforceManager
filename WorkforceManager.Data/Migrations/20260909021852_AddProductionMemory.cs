using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionMemories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RemindOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionMemories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionMemoryStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductionMemoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionMemoryStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionMemoryStages_ProductionMemories_ProductionMemoryId",
                        column: x => x.ProductionMemoryId,
                        principalTable: "ProductionMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionMemoryStages_ProductionStages_ProductionStageId",
                        column: x => x.ProductionStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMemories_CompletedAt_RemindOn",
                table: "ProductionMemories",
                columns: new[] { "CompletedAt", "RemindOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMemories_ProductId",
                table: "ProductionMemories",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMemoryStages_ProductionMemoryId_Position",
                table: "ProductionMemoryStages",
                columns: new[] { "ProductionMemoryId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionMemoryStages_ProductionStageId",
                table: "ProductionMemoryStages",
                column: "ProductionStageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionMemoryStages");

            migrationBuilder.DropTable(
                name: "ProductionMemories");
        }
    }
}
