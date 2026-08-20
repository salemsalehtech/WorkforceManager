using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// عمود ترتيب العمال المخصص (شاشة "ترتيب العمال" الجديدة) — بيحل
    /// محل الترتيب الأبجدي التلقائي في كل مكان بيعرض قايمة عمال.
    ///
    /// **بيانات موجودة بالفعل بتاخد ترتيب أبجدي كنقطة بداية** (نفس
    /// الترتيب اللي كانوا عليه أصلاً قبل الميزة دي)، فمفيش قفزة مفاجئة
    /// في ترتيب حد أول ما يفتح البرنامج — المدير من هنا يرتّبهم زي ما
    /// عايز من شاشة الترتيب. نفس أسلوب NormalizeStageSortOrder
    /// (ROW_NUMBER على نفس عمود القراءة الحالي).
    /// </summary>
    public partial class AddWorkerSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Workers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                WITH Ordered AS (
                    SELECT Id, ROW_NUMBER() OVER (ORDER BY FullName, Id) AS NewOrder
                    FROM Workers
                )
                UPDATE Workers
                SET SortOrder = (SELECT NewOrder FROM Ordered WHERE Ordered.Id = Workers.Id)
                WHERE Id IN (SELECT Id FROM Ordered);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Workers");
        }
    }
}
