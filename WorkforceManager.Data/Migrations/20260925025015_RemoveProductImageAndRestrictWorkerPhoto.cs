using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProductImageAndRestrictWorkerPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageData",
                table: "Products");

            // الصورة بقت حكرًا على الحسابات الإدارية (مدير/رئيس قسم،
            // HourlyRole = 5/6) — نمسح صور عمال الإنتاج العاديين الموجودة
            // فعلاً في القاعدة (بالقطعة أو بالساعة)، القاعدة دي متفروضة
            // كمان في WorkerManagementService.SetWorkerPhotoAsync.
            migrationBuilder.Sql(
                "UPDATE Workers SET PhotoData = NULL WHERE HourlyRole IS NULL OR HourlyRole NOT IN (5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ImageData",
                table: "Products",
                type: "BLOB",
                nullable: true);

            // ملحوظة: صور عمال الإنتاج اللي اتمسحت في Up() مش بترجع هنا —
            // الحذف مقصود ونهائي (بعد نسخة احتياطية إجبارية قبل الترحيل).
        }
    }
}
