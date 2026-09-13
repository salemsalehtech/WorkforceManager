using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// لقطة الإقفال بقت "دخل الخط كام" بدل "واقف كام".
    ///
    /// الاسم اتغير والمعنى اتغير معاه، فنقل القيمة القديمة زي ما هي كان
    /// هيسيب رقم غلط في تقرير المستخدم. القيمة الجديدة بتتحسب من سجلات
    /// الإنتاج نفسها: إنتاج **أول مرحلة** في كل منتج في اليوم المقفول —
    /// نفس التعريف اللي DailyProductionReportService شغال بيه دلوقتي.
    /// </summary>
    public partial class RenameParkedToStartedPieces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ParkedPieces",
                table: "ProductionDayClosures",
                newName: "StartedPieces");

            // "أول مرحلة" = أقل SortOrder، والتعادل بيتفض بالـ Id — نفس
            // ترتيب ActiveLine في الخدمة بالظبط، عشان الرقم المحفوظ يطابق
            // اللي التقرير هيحسبه لو اتسأل عن نفس اليوم
            migrationBuilder.Sql(@"
UPDATE ProductionDayClosures
SET StartedPieces = COALESCE((
    SELECT SUM(dp.PieceCount)
    FROM DailyProductions dp
    WHERE date(dp.Date) = date(ProductionDayClosures.Date)
      AND dp.IsDeleted = 0
      AND dp.ProductionStageId IN (
          SELECT (
              SELECT s.Id FROM ProductionStages s
              WHERE s.ProductId = p.Id AND s.IsActive = 1 AND s.IsDeleted = 0
              ORDER BY s.SortOrder, s.Id LIMIT 1
          )
          FROM Products p
          WHERE p.IsDeleted = 0
      )
), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // الرجوع بيرجّع الاسم بس — الواقف القديم متحسبش من سجلات، وحسابه
            // تاني هنا معناه إحياء منطق اتشال عن قصد
            migrationBuilder.RenameColumn(
                name: "StartedPieces",
                table: "ProductionDayClosures",
                newName: "ParkedPieces");
        }
    }
}
