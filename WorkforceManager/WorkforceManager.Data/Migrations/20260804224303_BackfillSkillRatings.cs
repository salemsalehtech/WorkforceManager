using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkforceManager.Data.Migrations
{
    /// <summary>
    /// يصحّح تقييمات المهارات اللي كانت موجودة قبل ما نظام التقييم يتضاف.
    ///
    /// المشكلة: الترحيل اللي ضاف عمود RatingValue حطّ للصفوف القديمة
    /// **صفر** (القيمة الافتراضية لأي رقم عشري)، مش 1.0 اللي الكود بيبدأ
    /// بيها. النتيجة إن كل عامل مربوط بمهارة من قبل الميزة بقى تقييمه 0%،
    /// وقايمة اختيار العمال مرتبة بالتقييم — فالترتيب بقى بلا معنى وكل
    /// العمال بيبانوا في أسوأ درجة.
    ///
    /// 1.0 معناها "بيعمل الكوتة المعيارية بالظبط" — نقطة البداية المحايدة
    /// الصح للي لسه ما اتقيّمش لا يدوي ولا تلقائي.
    /// </summary>
    public partial class BackfillSkillRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // <= 0 مش = 0: بيغطي كمان أي قيمة سالبة لو حصلت
            migrationBuilder.Sql("UPDATE WorkerSkills SET RatingValue = 1.0 WHERE RatingValue <= 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // مفيش رجوع: القيم القديمة (أصفار) كانت غلط، ورجوعها مش
            // استرجاع بيانات — ده إعادة إدخال الباج
        }
    }
}
