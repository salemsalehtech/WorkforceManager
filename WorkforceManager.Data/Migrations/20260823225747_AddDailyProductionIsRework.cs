using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// إعادة العمل: سجل إنتاج بيتحسب في يومية العامل وأجره، ومابيعدّش
    /// في إنتاج الخط الفعلي.
    ///
    /// الفهرس بيتبني من جديد بعمود خامس (IsRework) عن قصد: استعلام
    /// "الشغل الواقف" بيفلتر عليه دلوقتي، ولو مش جوّه الفهرس المغطّي
    /// SQLite هيرجع للجدول صف صف — نفس البُطء اللي الفهرس اتعمل عشانه
    /// أصلاً (شوف تعليق DailyProduction).
    /// </summary>
    public partial class AddDailyProductionIsRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount",
                table: "DailyProductions");

            migrationBuilder.AddColumn<bool>(
                name: "IsRework",
                table: "DailyProductions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework",
                table: "DailyProductions",
                columns: new[] { "ProductionStageId", "Date", "IsDeleted", "PieceCount", "IsRework" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount_IsRework",
                table: "DailyProductions");

            migrationBuilder.DropColumn(
                name: "IsRework",
                table: "DailyProductions");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProductions_ProductionStageId_Date_IsDeleted_PieceCount",
                table: "DailyProductions",
                columns: new[] { "ProductionStageId", "Date", "IsDeleted", "PieceCount" });
        }
    }
}
