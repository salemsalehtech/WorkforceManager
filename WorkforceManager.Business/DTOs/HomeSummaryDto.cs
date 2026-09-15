namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// ملخص شاشة الرئيسية — حزمة واحدة من أرقام جاهزة أصلاً من خدمات
    /// موجودة (WeeklySummaryService, InitialBalanceService,
    /// ProductionMemoryService)، مفيش رقم محسوب هنا من جديد. شوف
    /// HomeSummaryService.GetSummaryAsync لمصدر كل حقل.
    /// </summary>
    public class HomeSummaryDto
    {
        /// <summary>أول يوم في أسبوع الشغل الحالي (الخميس)</summary>
        public DateTime WeekStart { get; init; }

        /// <summary>آخر يوم في أسبوع الشغل الحالي (الأربع)</summary>
        public DateTime WeekEnd { get; init; }

        public int TotalPiecesThisWeek { get; init; }

        public int ActiveWorkersThisWeek { get; init; }

        public decimal NetWorkdaysThisWeek { get; init; }

        /// <summary>أول عامل في ترتيب "أحسن 3 عمال" الأسبوعي — null لو محدش حقق الشرط لسه</summary>
        public WorkerWeeklySummaryDto? BestWorkerOfWeek { get; init; }

        /// <summary>أرصدة أولية لسه مفتوحة (مش Completed) وعمرها HomeSummaryService.StaleInitialBalanceDays يوم فأكتر</summary>
        public IReadOnlyList<InitialBalanceDto> StaleInitialBalances { get; init; } = [];

        /// <summary>عدد خطط الذاكرة المستحقة أو المتأخرة اليوم</summary>
        public int DueMemoriesCount { get; init; }

        /// <summary>إجمالي قطع الأسبوع السابق (نفس مدى TotalPiecesThisWeek بس قبله بأسبوع) — لعرض نسبة التغيير</summary>
        public int PreviousWeekTotalPieces { get; init; }

        /// <summary>اسم المنتج اللي اتشتغل فيه أكتر قطع الأسبوع ده — null لو مفيش إنتاج خالص</summary>
        public string? TopProductName { get; init; }

        public int TopProductPieces { get; init; }

        /// <summary>نسبة الحضور الأسبوعية (حاضر ÷ (حاضر + غايب بإذن + غايب بدون إذن) × 100) — null لو مفيش أي سجل حضور اتسجل الأسبوع ده</summary>
        public decimal? AttendanceRatePercent { get; init; }
    }
}
