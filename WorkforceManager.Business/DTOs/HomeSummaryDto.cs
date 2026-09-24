namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// ملخص شاشة الرئيسية — حزمة واحدة من أرقام جاهزة أصلاً من خدمات
    /// موجودة (WeeklySummaryService, InitialBalanceService,
    /// ProductionMemoryService, ProductActivityService، سجلات الحضور)،
    /// مفيش رقم محسوب هنا من جديد. شوف
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

        /// <summary>
        /// خطط الذاكرة النشطة اللي تذكيرها فات أو النهارده أو خلال
        /// HomeDashboardRules.MemoryDueSoonDays — الأقرب الأول. الكارت ده
        /// إضافة جنب ديالوج التذكير عند بدء التشغيل، مش بديل عنه.
        /// </summary>
        public IReadOnlyList<ProductionMemoryDto> DueSoonMemories { get; init; } = [];

        /// <summary>إجمالي قطع الأسبوع السابق (نفس مدى TotalPiecesThisWeek بس قبله بأسبوع) — لعرض نسبة التغيير</summary>
        public int PreviousWeekTotalPieces { get; init; }

        /// <summary>عدد العمال النشطين الأسبوع اللي فات — نفس تعريف ActiveWorkersThisWeek</summary>
        public int PreviousWeekActiveWorkers { get; init; }

        /// <summary>صافي يوميات الأسبوع اللي فات — نفس تعريف NetWorkdaysThisWeek</summary>
        public decimal PreviousWeekNetWorkdays { get; init; }

        /// <summary>أيام الغياب بدون إذن الأسبوع ده (مجموع AbsentWithoutPermissionDays من الملخص الأسبوعي — الحسابات الإدارية مستبعدة زي هناك)</summary>
        public int UnexcusedAbsencesThisWeek { get; init; }

        public int PreviousWeekUnexcusedAbsences { get; init; }

        /// <summary>أكتر منتج إنتاجًا الأسبوع ده — null لو مفيش إنتاج خالص</summary>
        public HomeProductStat? TopProduct { get; init; }

        /// <summary>أقل منتج إنتاجًا — null لو أقل من منتجين اشتغلوا (شوف HomeDashboardRules.PickBottomProduct)</summary>
        public HomeProductStat? BottomProduct { get; init; }

        /// <summary>قطع أحسن عامل الأسبوع اللي فات (0 لو ماكانش ليه نشاط) — لسهم المقارنة على كارته</summary>
        public int BestWorkerPreviousPieces { get; init; }

        /// <summary>
        /// "الأقل أداءً الأسبوع ده" — آخر واحد في WorkerRecognitionRules.Rank،
        /// null لو أقل من HomeDashboardRules.MinRankedForWorstWorker في الترتيب
        /// </summary>
        public WorkerWeeklySummaryDto? WorstWorkerOfWeek { get; init; }

        public int WorstWorkerPreviousPieces { get; init; }

        /// <summary>سلسلة الالتزام: أيام شغل متتالية من غير غياب بدون إذن (شوف HomeDashboardRules.ComputeStreak)</summary>
        public int StreakDays { get; init; }

        /// <summary>السلسلة أطول من مدى البحث — الواجهة بتعرض "+"</summary>
        public bool StreakIsCapped { get; init; }

        /// <summary>نسبة الحضور الأسبوعية (حاضر ÷ (حاضر + غايب بإذن + غايب بدون إذن) × 100) — null لو مفيش أي سجل حضور اتسجل الأسبوع ده</summary>
        public decimal? AttendanceRatePercent { get; init; }

        /// <summary>
        /// نسبة محقق الخطة الشهرية إجمالاً لحد النهارده — نفس تعريف نسبة
        /// المحقق في شاشة الخطة الشهرية (محقق فعلي ÷ خطة متناسبة مع أيام
        /// الشغل المنقضية)، بس على مستوى كل المنتجات مع بعض. null لو مفيش
        /// خطة مسجّلة للشهر ده خالص (مش صفر — الصفر بيبقى "خطة موجودة
        /// ومفيش محقق").
        /// </summary>
        public decimal? MonthlyPlanAchievedPercent { get; init; }
    }

    /// <summary>منتج واحد في كروت الرئيسية — قطعه المكتملة الأسبوع ده واللي فات (من ProductActivityService)</summary>
    public record HomeProductStat(int ProductId, string ProductName, int Pieces, int PreviousPieces);
}
