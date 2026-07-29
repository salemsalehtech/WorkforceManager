using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// القاعدة الوحيدة لتكلفة يوم الغياب بدون إذن: **نص يومية، مرة واحدة بس**.
    ///
    /// ليه محتاجين قاعدة مشتركة أصلاً؟ لأن الخصم دلوقتي ليه طريقين:
    ///
    /// 1. الطريق القديم — خصم مدفون في الحساب (عدد أيام الغياب × 0.5)،
    ///    مكانش بيظهر للعامل في أي مكان.
    /// 2. الطريق الجديد — جزاء تلقائي (<see cref="PenaltySource.AutoAbsence"/>)
    ///    بيتولّد مع حفظ الحضور، وبيظهر في تبويب الجزاءات وفي قسيمة الأجر.
    ///
    /// لو الاتنين اشتغلوا مع بعض العامل هيتخصم منه يومية كاملة بدل نص —
    /// وده مش المطلوب. عشان كده يوم الغياب بيتخصم **بالجزاء** لو ليه
    /// جزاء تلقائي، و**بالطريق القديم** لو مالوش. النتيجة نص يومية في
    /// الحالتين، والأيام القديمة المسجّلة قبل الميزة دي بتفضل محسوبة صح
    /// من غير ما نلمس بياناتها.
    /// </summary>
    public static class AbsenceDeductionRule
    {
        /// <summary>خصم الغياب بدون إذن عن اليوم الواحد = نص يومية</summary>
        public const decimal UnexcusedAbsencePerDay = 0.5m;

        /// <summary>مقدار الخصم المقابل ليوم غياب بدون إذن (نص يومية)</summary>
        public const PenaltyDeduction AbsenceDeductionAmount = PenaltyDeduction.HalfDay;

        /// <summary>السبب المكتوب على الجزاء التلقائي — ثابت عشان يتعرف ويتقري بنفس الشكل في كل مكان</summary>
        public const string AutoAbsenceReason = "غياب بدون إذن (خصم تلقائي)";

        /// <summary>
        /// خصم الغياب المحسوب بالطريق القديم: أيام الغياب بدون إذن اللي
        /// **مالهاش** جزاء تلقائي بس. الأيام اللي ليها جزاء بيتخصموا من
        /// خلال الجزاء نفسه، فلو عدّيناهم هنا كمان هيبقى خصم مزدوج.
        /// </summary>
        public static decimal ComputeUnpenalizedAbsenceDeduction(
            IEnumerable<Attendance> attendance, IEnumerable<Penalty> penalties)
        {
            var autoPenaltyDays = penalties
                .Where(p => p.Source == PenaltySource.AutoAbsence)
                .Select(p => p.Date.Date)
                .ToHashSet();

            var unpenalizedDays = attendance
                .Where(a => a.Status == AttendanceStatus.AbsentWithoutPermission)
                .Count(a => !autoPenaltyDays.Contains(a.Date.Date));

            return unpenalizedDays * UnexcusedAbsencePerDay;
        }
    }
}
