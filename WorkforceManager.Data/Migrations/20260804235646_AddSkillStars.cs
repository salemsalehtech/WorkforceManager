using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// تحويل تقييم المهارة لنظام النجوم: رأي المدير منفصل عن قياس النظام.
    ///
    /// **الملف ده متكتوب بالإيد** — EF ولّد نسخة فيها إعادتين تسمية غلط
    /// كانوا هيبوّظوا البيانات:
    ///   • RatingSource → Stars: العمود ده رقم enum (1 أو 2)، فكل العمال
    ///     كانوا هياخدوا نجمة أو نجمتين بدل التقييم المحايد (3).
    ///   • LastManualValue → StarsUpdatedAt: عمود عشري بيتحوّل لعمود
    ///     تاريخ — قيم مالهاش أي معنى في خانة وقت.
    ///
    /// الصح إن الأعمدة اللي معناها اتغيّر بس هي اللي تتعاد تسميتها،
    /// واللي مفهومها اتلغى يتشال، والجديد يتضاف بقيمه الصح.
    /// </summary>
    public partial class AddSkillStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- أعمدة نفس معناها، اسمها بس اتوضّح ----------
            // القياس بقى اسمه صريح إنه "مقاس" مش "تقييم"
            migrationBuilder.RenameColumn(
                name: "RatingValue",
                table: "WorkerSkills",
                newName: "MeasuredRatio");

            migrationBuilder.RenameColumn(
                name: "LastAutoCalculatedAt",
                table: "WorkerSkills",
                newName: "MeasuredAt");

            migrationBuilder.RenameColumn(
                name: "AutoSampleDays",
                table: "WorkerSkills",
                newName: "MeasuredDays");

            // ---------- مفاهيم اتلغت ----------
            // مبقاش فيه "مصدر" للتقييم: النجوم دايمًا من المدير،
            // والقياس دايمًا من النظام
            migrationBuilder.DropColumn(name: "RatingSource", table: "WorkerSkills");
            migrationBuilder.DropColumn(name: "LastManualValue", table: "WorkerSkills");

            // ---------- تقييم المدير بالنجوم ----------
            migrationBuilder.AddColumn<int>(
                name: "Stars",
                table: "WorkerSkills",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);   // 3 = بيعمل الكوتة — نقطة البداية المحايدة

            migrationBuilder.AddColumn<DateTime>(
                name: "StarsUpdatedAt",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StarsUpdatedBy",
                table: "WorkerSkills",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            // ---------- نقل التقييم القديم للنجوم ----------
            // المهارات القديمة عندها Level (مبتدئ/متمكن/خبير). أي إشارة
            // موجودة أحسن من تصفير الكل على 3 — العامل اللي كان متعلّم
            // "خبير" ميرجعش عادي من غير سبب.
            // القيم: Beginner=1, Proficient=2, Expert=3
            migrationBuilder.Sql("UPDATE WorkerSkills SET Stars = 4 WHERE Level = 3;");
            migrationBuilder.Sql("UPDATE WorkerSkills SET Stars = 3 WHERE Level = 2;");
            migrationBuilder.Sql("UPDATE WorkerSkills SET Stars = 2 WHERE Level = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Stars", table: "WorkerSkills");
            migrationBuilder.DropColumn(name: "StarsUpdatedAt", table: "WorkerSkills");
            migrationBuilder.DropColumn(name: "StarsUpdatedBy", table: "WorkerSkills");

            migrationBuilder.AddColumn<int>(
                name: "RatingSource",
                table: "WorkerSkills",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "LastManualValue",
                table: "WorkerSkills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "MeasuredRatio",
                table: "WorkerSkills",
                newName: "RatingValue");

            migrationBuilder.RenameColumn(
                name: "MeasuredAt",
                table: "WorkerSkills",
                newName: "LastAutoCalculatedAt");

            migrationBuilder.RenameColumn(
                name: "MeasuredDays",
                table: "WorkerSkills",
                newName: "AutoSampleDays");
        }
    }
}
