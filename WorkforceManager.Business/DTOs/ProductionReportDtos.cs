using WorkforceManager.Core.Helpers;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>سطر إنتاج مرحلة داخل التقارير: منتج/مرحلة بعدد القطع واليوميات</summary>
    public class ProductStageProductionDto
    {
        public string ProductName { get; init; } = string.Empty;
        public string StageName { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public int Pieces { get; init; }
        public decimal Workdays { get; init; }

        /// <summary>هل دي آخر مرحلة للمنتج؟ (القطع عليها = إنتاج مكتمل خرج من الخط)</summary>
        public bool IsLastStage { get; init; }
    }

    /// <summary>ملخص إنتاج عامل داخل التقرير العام</summary>
    public class WorkerProductionSummaryDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public bool IsHourly { get; init; }
        public int TotalPieces { get; init; }
        public decimal TotalWorkdays { get; init; }
    }

    /// <summary>
    /// التقرير العام للإنتاج عن فترة [from, to]: ملخص إجمالي للقسم +
    /// تفصيل بالمنتج/المرحلة + تفصيل بالعامل.
    /// </summary>
    public class GeneralProductionReportDto
    {
        public DateTime From { get; init; }
        public DateTime To { get; init; }

        // ------- الملخص الإجمالي -------
        /// <summary>إجمالي القطع المكتملة (على آخر مرحلة لكل منتج) — الإنتاج اللي خرج فعلاً</summary>
        public int TotalCompletedPieces { get; init; }

        /// <summary>إجمالي اليوميات المنتجة في الفترة (كل المراحل + شغل الساعة)</summary>
        public decimal TotalWorkdays { get; init; }

        /// <summary>عدد العمال اللي اشتغلوا في الفترة</summary>
        public int WorkersCount { get; init; }

        /// <summary>عدد الأيام اللي فيها إنتاج فعلي</summary>
        public int ProductionDays { get; init; }

        // ------- التفصيل -------
        public List<ProductStageProductionDto> ByProductStage { get; init; } = new();
        public List<WorkerProductionSummaryDto> ByWorker { get; init; } = new();
    }

    /// <summary>إنتاج يوم واحد في تقرير العامل</summary>
    public class WorkerDayProductionDto
    {
        public DateTime Date { get; init; }
        public int Pieces { get; init; }
        public decimal Workdays { get; init; }

        /// <summary>تفصيل اليوم (المنتجات/المراحل أو شغل الساعة) كنص</summary>
        public string Detail { get; init; } = string.Empty;
    }

    /// <summary>
    /// تقرير عامل معين عن فترة [from, to]: إنتاجه بالتفصيل (بالمنتج/المرحلة
    /// وباليوم) + الحضور والغياب + الأجر والجزاءات.
    /// </summary>
    public class WorkerProductionReportDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public bool IsHourly { get; init; }
        public string TypeText { get; init; } = string.Empty;
        public decimal DailyWageEgp { get; init; }

        public DateTime From { get; init; }
        public DateTime To { get; init; }

        // ------- الإنتاج -------
        public int TotalPieces { get; init; }
        public decimal ProducedWorkdays { get; init; }
        public List<ProductStageProductionDto> ByProductStage { get; init; } = new();
        public List<WorkerDayProductionDto> ByDay { get; init; } = new();

        // ------- الحضور والغياب -------
        public int PresentDays { get; init; }
        public int AbsentWithPermissionDays { get; init; }
        public int AbsentWithoutPermissionDays { get; init; }
        public decimal AbsenceDeduction { get; init; }

        // ------- الأجر والجزاءات -------
        public decimal PenaltyDeduction { get; init; }
        public List<PenaltySummaryDto> Penalties { get; init; } = new();

        // ------- السلف والحوافز (بالجنيه) -------
        /// <summary>إجمالي الحوافز/المكافآت بالجنيه (تُضاف للأجر)</summary>
        public decimal BonusEgp { get; init; }

        /// <summary>إجمالي السلف/المسحوبات بالجنيه (تُخصم من الأجر)</summary>
        public decimal AdvanceEgp { get; init; }

        /// <summary>تفصيل حركات السلف والحوافز في الفترة</summary>
        public List<WageAdjustmentSummaryDto> Adjustments { get; init; } = new();

        /// <summary>صافي يوميات الفترة = المنتج − خصم الغياب − خصم الجزاءات</summary>
        public decimal NetWorkdays => ProducedWorkdays - AbsenceDeduction - PenaltyDeduction;

        /// <summary>أجر اليوميات بالجنيه = الصافي × سعر اليومية (قبل السلف والحوافز)</summary>
        public decimal WorkdaysWageEgp => NetWorkdays * DailyWageEgp;

        /// <summary>الأجر النهائي بالجنيه = أجر اليوميات + الحوافز − السلف</summary>
        public decimal NetWageEgp => WorkdaysWageEgp + BonusEgp - AdvanceEgp;

        // ------- المهارات -------

        /// <summary>
        /// تقييم العامل على المنتجات اللي عنده فيها مهارة، الأعلى الأول.
        ///
        /// جزء من التقرير عن قصد: "أنتج كام" لوحده مبيقولش هو شاطر في إيه.
        /// عامل إنتاجه قليل بس تقييمه ممتاز على مرحلة صعبة صورة مختلفة
        /// تمامًا عن عامل إنتاجه قليل وتقييمه ضعيف.
        /// </summary>
        public List<WorkerSkillSummaryDto> Skills { get; init; } = new();

        // ------- تحذيرات -------

        /// <summary>
        /// السلف أكلت الأجر كله والصافي طلع بالسالب.
        ///
        /// الرقم السالب بيتعرض زي ما هو مع تحذير صريح بدل ما يتصفّر:
        /// "العامل مدين" حقيقة محاسبية لازم تتشاف، وتصفيرها بيخفي إن
        /// السلف اتصرفت فعلاً.
        /// </summary>
        public bool IsWageNegative => NetWageEgp < 0;

        /// <summary>مفيش سعر يومية متسجّل — الأجر هيطلع صفر مهما أنتج</summary>
        public bool HasNoWageRate => DailyWageEgp <= 0;
    }

    /// <summary>تقييم عامل على منتج واحد — سطر في قسم المهارات</summary>
    public class WorkerSkillSummaryDto
    {
        public string ProductName { get; init; } = string.Empty;

        /// <summary>متوسط نجومه على مراحل المنتج اللي بيعرفها (1–5)</summary>
        public decimal AverageStars { get; init; }

        /// <summary>عدد مراحل المنتج اللي بيعرفها</summary>
        public int KnownStages { get; init; }

        public int RoundedStars =>
            Math.Clamp((int)Math.Round(AverageStars, MidpointRounding.AwayFromZero), 1, 5);

        public string StarsText => RtlSafeText.Stars(RoundedStars);

        public string StagesText => $"{KnownStages} مرحلة";
    }

    /// <summary>سطر حركة سلفة/حافز داخل تقرير العامل وقسيمة أجره</summary>
    public class WageAdjustmentSummaryDto
    {
        public int AdjustmentId { get; init; }
        public DateTime Date { get; init; }
        public Core.Enums.WageAdjustmentType Type { get; init; }
        public decimal AmountEgp { get; init; }
        public string? Note { get; init; }
    }
}
