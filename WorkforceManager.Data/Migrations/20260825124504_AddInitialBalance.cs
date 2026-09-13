using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework",
                table: "DailyProductions");

            migrationBuilder.AddColumn<bool>(
                name: "IsBalanceCompletion",
                table: "DailyProductions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InitialBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalDailyProductionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DeletionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DeletedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialBalances", x => x.Id);
                    table.CheckConstraint("CK_InitialBalance_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InitialBalances_DailyProductions_OriginalDailyProductionId",
                        column: x => x.OriginalDailyProductionId,
                        principalTable: "DailyProductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InitialBalances_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitialBalanceRanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InitialBalanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    PieceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialBalanceRanges", x => x.Id);
                    table.CheckConstraint("CK_InitialBalanceRange_PieceCount", "[PieceCount] > 0");
                    table.ForeignKey(
                        name: "FK_InitialBalanceRanges_InitialBalances_InitialBalanceId",
                        column: x => x.InitialBalanceId,
                        principalTable: "InitialBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InitialBalanceRanges_ProductionStages_FromStageId",
                        column: x => x.FromStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialBalanceRanges_ProductionStages_ToStageId",
                        column: x => x.ToStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitialBalanceUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InitialBalanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    InitialBalanceRangeId = table.Column<int>(type: "INTEGER", nullable: true),
                    UsedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductionStageId = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyProductionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialBalanceUsages", x => x.Id);
                    table.CheckConstraint("CK_InitialBalanceUsage_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_InitialBalanceUsages_DailyProductions_DailyProductionId",
                        column: x => x.DailyProductionId,
                        principalTable: "DailyProductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialBalanceUsages_InitialBalanceRanges_InitialBalanceRangeId",
                        column: x => x.InitialBalanceRangeId,
                        principalTable: "InitialBalanceRanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InitialBalanceUsages_InitialBalances_InitialBalanceId",
                        column: x => x.InitialBalanceId,
                        principalTable: "InitialBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialBalanceUsages_ProductionStages_ProductionStageId",
                        column: x => x.ProductionStageId,
                        principalTable: "ProductionStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialBalanceUsages_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework_IsBalanceCompletion",
                table: "DailyProductions",
                columns: new[] { "ProductionStageId", "Date", "IsDeleted", "PieceCount", "IsRework", "IsBalanceCompletion" });

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceRanges_FromStageId",
                table: "InitialBalanceRanges",
                column: "FromStageId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceRanges_InitialBalanceId",
                table: "InitialBalanceRanges",
                column: "InitialBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceRanges_ToStageId",
                table: "InitialBalanceRanges",
                column: "ToStageId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalances_OriginalDailyProductionId",
                table: "InitialBalances",
                column: "OriginalDailyProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalances_ProductId_OriginalDate",
                table: "InitialBalances",
                columns: new[] { "ProductId", "OriginalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_DailyProductionId",
                table: "InitialBalanceUsages",
                column: "DailyProductionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_InitialBalanceId",
                table: "InitialBalanceUsages",
                column: "InitialBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_InitialBalanceRangeId",
                table: "InitialBalanceUsages",
                column: "InitialBalanceRangeId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_ProductionStageId",
                table: "InitialBalanceUsages",
                column: "ProductionStageId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialBalanceUsages_WorkerId",
                table: "InitialBalanceUsages",
                column: "WorkerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InitialBalanceUsages");

            migrationBuilder.DropTable(
                name: "InitialBalanceRanges");

            migrationBuilder.DropTable(
                name: "InitialBalances");

            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework_IsBalanceCompletion",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "IsBalanceCompletion",
                table: "DailyProductions");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework",
                table: "DailyProductions",
                columns: new[] { "ProductionStageId", "Date", "IsDeleted", "PieceCount", "IsRework" });
        }
    }
}
