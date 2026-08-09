using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// إعادة ترقيم مراحل كل منتج من 1 بالترتيب الحالي.
    ///
    /// بيانات قديمة فيها مراحل ترتيبها 0 أو فيه فجوات أو تكرار — البرنامج
    /// دلوقتي بينشئ المراحل من 1 (ProductManagementService)، وMoveStage
    /// بيعيد الترقيم من 1 بعد أي حركة، فالبيانات القديمة بس هي اللي شاذة.
    ///
    /// **مفيش ترتيب بيتغيّر هنا.** الترقيم الجديد بيتبني على نفس الترتيب
    /// اللي البرنامج بيقرا بيه أصلاً (SortOrder وبعدين Id)، فآخر مرحلة
    /// بتفضل آخر مرحلة وكل الأرقام في التقارير زي ما هي. اللي بيتغيّر إن
    /// الأرقام تبقى 1، 2، 3… من غير صفر ولا فجوات.
    /// </summary>
    public partial class NormalizeStageSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ROW_NUMBER بيرقّم جوّه كل منتج على حدة بنفس ترتيب القراءة
            migrationBuilder.Sql(@"
                WITH Ordered AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ProductId
                               ORDER BY SortOrder, Id
                           ) AS NewOrder
                    FROM ProductionStages
                )
                UPDATE ProductionStages
                SET SortOrder = (SELECT NewOrder FROM Ordered WHERE Ordered.Id = ProductionStages.Id)
                WHERE Id IN (SELECT Id FROM Ordered);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // الأرقام الأصلية مش متسجّلة في أي مكان، والترتيب النسبي محفوظ
            // زي ما هو — فمفيش حاجة تترجّع، ومفيش حاجة اتفقدت.
        }
    }
}
