using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// المصدر الوحيد لحالات الحضور المعروضة للمستخدم. الشاشة بتسأل من هنا
    /// بدل ما تكتب الحالات بإيدها، فأي حالة تتضاف لـ
    /// <see cref="AttendanceStatus"/> بتظهر في الواجهة لوحدها.
    ///
    /// الحالات بتترشّح حسب نوع العامل (بالقطعة / بالساعة). النهارده
    /// النوعين بياخدوا نفس التلات حالات — بس الترشيح موجود عشان لما
    /// يتقرر إن العامل بالساعة له حالات خاصة بيه، التغيير يبقى في الدالة
    /// دي بس مش في كل شاشة بتعرض حالة.
    /// </summary>
    public static class AttendanceStatusCatalog
    {
        /// <summary>الاسم العربي المعروض لكل حالة</summary>
        public static string ToArabicName(this AttendanceStatus status) => status switch
        {
            AttendanceStatus.Present => "حاضر",
            AttendanceStatus.AbsentWithPermission => "غياب بإذن",
            AttendanceStatus.AbsentWithoutPermission => "غياب بدون إذن",
            _ => status.ToString()
        };

        /// <summary>
        /// الحالات المتاحة لعامل حسب نوعه، بترتيب العرض.
        /// </summary>
        /// <param name="isHourly">عامل بالساعة (له دور رص/جودة/تدريب) ولا بالقطعة</param>
        public static IReadOnlyList<AttendanceStatus> ForWorker(bool isHourly)
        {
            // القراءة من الـ enum نفسه (تعريف الحالات في النظام) مش من قائمة مكتوبة بالإيد
            var allStatuses = Enum.GetValues<AttendanceStatus>();

            // النوعين حاليًا بياخدوا نفس المجموعة — نقطة الترشيح الوحيدة لو اتغير ده
            _ = isHourly;

            return allStatuses;
        }

        /// <summary>هل الحالة دي بتستاهل جزاء غياب تلقائي؟ (نص يومية)</summary>
        public static bool TriggersAbsencePenalty(AttendanceStatus status) =>
            status == AttendanceStatus.AbsentWithoutPermission;
    }
}
