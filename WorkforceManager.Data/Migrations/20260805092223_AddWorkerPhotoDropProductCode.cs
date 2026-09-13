using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// حاجتين مع بعض:
    ///
    /// **مسح كود المنتج** — الخانة اتشالت من نافذة إضافة/تعديل المنتج
    /// بطلب المستخدم. اتأكدنا الأول إن مفيش حاجة بتقراه: مفيش تقرير ولا
    /// تصدير اكسل ولا حساب بيلمسه، رحلته كانت النافذة ← الخدمة ← عرض في
    /// الكارت. فاتمسح من الداتابيز كمان بدل ما يفضل عمود ميت. القيم
    /// القديمة بتضيع عن قصد وبموافقة صريحة.
    ///
    /// **صورة العامل** — عمود BLOB زي Products.ImageData بالظبط: جوه
    /// قاعدة البيانات مش ملف على الجنب، عشان النسخة الاحتياطية (اللي
    /// بتنسخ ملف الـ db بس) تفضل كاملة.
    ///
    /// ملحوظة: Workers.SkillsNotes **متمسحش** رغم إن خانته اتشالت من
    /// الفورم — DatabaseSeeder.SeedHourlyRolesAsync لسه بيقراه عشان
    /// يصنّف عمال الرص/الجودة/التدريب، والبحث بيدوّر جواه.
    /// </summary>
    public partial class AddWorkerPhotoDropProductCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "Products");

            migrationBuilder.AddColumn<byte[]>(
                name: "PhotoData",
                table: "Workers",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoData",
                table: "Workers");

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "Products",
                type: "TEXT",
                maxLength: 30,
                nullable: true);
        }
    }
}
