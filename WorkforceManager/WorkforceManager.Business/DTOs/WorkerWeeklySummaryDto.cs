using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// ملخص أسبوع كامل لعامل واحد (الأسبوع من الخميس للأربع): إجمالي
    /// اليوميات اللي أنتجها، وكل الخصومات (غياب بدون إذن = نص يوم عن كل
    /// يوم، والجزاءات بقيمها)، والصافي النهائي اللي بيتحاسب عليه.
    /// هذا هو الشكل اللي بيظهر في شاشة العمال وتقرير الأسبوع وبروفايل العامل.
    /// </summary>
    public class WorkerWeeklySummaryDto
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;

        /// <summary>أول يوم في الأسبوع (الخميس)</summary>
        public DateTime WeekStart { get; set; }

        /// <summary>آخر يوم في الأسبوع (الأربع)</summary>
        public DateTime WeekEnd { get; set; }

        // ------- الإنتاج -------

        /// <summary>إجمالي اليوميات المنتجة فعليًا خلال الأسبوع (قبل أي خصم)</summary>
        public decimal ProducedWorkdays { get; set; }

        /// <summary>إجمالي عدد القطع خلال الأسبوع</summary>
        public int TotalPieces { get; set; }

        /// <summary>تفصيل الإنتاج حسب كل منتج/مرحلة اشتغل عليها خلال الأسبوع</summary>
        public List<StageBreakdownDto> Breakdown { get; set; } = new();

        // ------- الشغل بالساعة (للعمال بالساعة) -------

        /// <summary>هل هذا عامل بالساعة؟ (يوميّاته من الساعات مش من الإنتاج)</summary>
        public bool IsHourly { get; set; }

        /// <summary>عدد أيام الشغل المسجّلة بالساعة خلال الأسبوع</summary>
        public int HourlyDaysWorked { get; set; }

        /// <summary>إجمالي اليوميات من الشغل بالساعة خلال الأسبوع (داخلة في ProducedWorkdays)</summary>
        public decimal HourlyWorkdays { get; set; }

        // ------- الحضور والغياب -------

        /// <summary>عدد أيام الحضور المسجّلة خلال الأسبوع</summary>
        public int PresentDays { get; set; }

        /// <summary>عدد أيام الغياب بإذن (محايدة — من غير أي خصم)</summary>
        public int AbsentWithPermissionDays { get; set; }

        /// <summary>عدد أيام الغياب بدون إذن (كل يوم بيتخصم عنه نص يومية)</summary>
        public int AbsentWithoutPermissionDays { get; set; }

        /// <summary>خصم الغياب بدون إذن = عدد أيامه × 0.5 يومية</summary>
        public decimal AbsenceDeduction { get; set; }

        // ------- الجزاءات -------

        /// <summary>تفاصيل جزاءات الأسبوع (السبب + قيمة الخصم) لعرضها في التقرير</summary>
        public List<PenaltySummaryDto> Penalties { get; set; } = new();

        /// <summary>إجمالي خصم الجزاءات باليوميات</summary>
        public decimal PenaltyDeduction { get; set; }

        // ------- الصافي -------

        /// <summary>
        /// صافي يوميات الأسبوع = المنتج − خصم الغياب − خصم الجزاءات.
        /// ده الرقم النهائي اللي بيتحاسب بيه العامل وبيترتب بيه في تقييم الأسبوع.
        /// </summary>
        public decimal NetWorkdays => ProducedWorkdays - AbsenceDeduction - PenaltyDeduction;

        // ------- الأجر بالجنيه -------

        /// <summary>سعر يومية العامل بالجنيه (الحالي)</summary>
        public decimal DailyWageEgp { get; set; }

        /// <summary>أجر الأسبوع بالجنيه = صافي اليوميات × سعر اليومية</summary>
        public decimal NetWageEgp => NetWorkdays * DailyWageEgp;

        /// <summary>هل هذا العامل من ضمن أحسن 3 عمال في الأسبوع؟ (WorkerRecognitionRules.Rank)</summary>
        public bool IsBestWorkerOfWeek { get; set; }

        /// <summary>
        /// ترتيبه الفعلي بين الفايزين (1/2/3) — null لغير الفايزين. غير
        /// مخزّن في قاعدة البيانات، بيتحط وقت العرض بس عشان الكارت يعرف
        /// مين الأول فعليًا حسب درجة الترتيب مش حسب ترتيبه في القايمة.
        /// </summary>
        public int? RecognitionRank { get; set; }
    }

    /// <summary>سطر جزاء واحد داخل الملخص الأسبوعي (للعرض في التقرير)</summary>
    public class PenaltySummaryDto
    {
        public int PenaltyId { get; set; }
        public DateTime Date { get; set; }
        public string Reason { get; set; } = string.Empty;
        public PenaltyDeduction Deduction { get; set; }

        /// <summary>قيمة الخصم باليوميات (محسوبة من نوع الجزاء)</summary>
        public decimal DeductedWorkdays => Deduction.ToWorkdays();

        /// <summary>اسم الخصم بالعربي للعرض ("نص يوم"، "يوم"، ...)</summary>
        public string DeductionName => Deduction.ToArabicName();
    }
}
