namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// كل أرقام تبويب "الإحصائيات" لشهر واحد حتى تاريخ معيّن — مبنية فوق
    /// صفوف MonthlyPlanTrackingService نفسها (مفيش حساب تاني للمحقق/الخطة)،
    /// زائد سلسلة يوم بيوم من نفس جدول المحقق اليدوي. شوف
    /// MonthlyPlanStatisticsMath للتعريفات بالظبط.
    /// </summary>
    public class MonthlyPlanStatisticsDto
    {
        public int Year { get; init; }
        public int Month { get; init; }

        /// <summary>آخر يوم محسوب (النهارده لو الشهر الحالي، آخر الشهر لو فات)</summary>
        public DateTime AsOfDate { get; init; }

        public int TotalWorkdays { get; init; }
        public int ElapsedWorkdays { get; init; }
        public int RemainingWorkdays { get; init; }

        public int TotalPlanned { get; init; }
        public int TotalAchieved { get; init; }

        /// <summary>الخطة بالتناسب مع الأيام المنقضية — "المفروض كنا وصلنا كام لحد النهارده"</summary>
        public decimal ProRatedPlan { get; init; }

        /// <summary>المحقق ÷ خطة الشهر كلها — null لو مفيش خطة</summary>
        public decimal? CompletionPercent { get; init; }

        /// <summary>المحقق ÷ المفروض لحد النهارده — null لو مفيش خطة أو لسه مفيش أيام منقضية</summary>
        public decimal? PacePercent { get; init; }

        /// <summary>المحقق − المفروض لحد النهارده (سالب = متأخر)</summary>
        public int PaceGap { get; init; }

        /// <summary>الأيام المنقضية ÷ أيام الشهر — مكان "المفروض" على شريط الإنجاز</summary>
        public decimal ExpectedPercent { get; init; }

        public int? Forecast { get; init; }
        public int? ForecastGap { get; init; }

        public decimal? AverageDaily { get; init; }
        public int? RequiredDaily { get; init; }

        public int PreviousSameDay { get; init; }
        public decimal? ChangeVsPrevious { get; init; }

        public decimal TotalWeightGrams { get; init; }
        public int ProductsWithoutWeight { get; init; }

        public int AheadCount { get; init; }
        public int OnTrackCount { get; init; }
        public int BehindCount { get; init; }
        public int OutsidePlanCount { get; init; }

        /// <summary>أيام الشغل المنقضية اللي اتسجّل فيها أي إنتاج يدوي</summary>
        public int RecordedWorkdays { get; init; }

        public DateTime? BestDayDate { get; init; }
        public int BestDayQuantity { get; init; }

        public List<MonthlyPlanStatisticsDayDto> Days { get; init; } = new();
        public List<MonthlyPlanStatisticsWeekdayDto> Weekdays { get; init; } = new();
        public List<MonthlyPlanStatisticsFamilyDto> Families { get; init; } = new();

        /// <summary>منتجات ليها خطة أو إنتاج أو إنتاج الشهر اللي فات — المنتجات الفاضية تمامًا مستبعدة</summary>
        public List<MonthlyPlanTrackingDto> Products { get; init; } = new();

        /// <summary>الإنتاج الحقيقي والهالك لكل منتج في Products (صفر لو مفيش)</summary>
        public Dictionary<int, MonthlyPlanStatisticsRealDto> RealByProduct { get; init; } = new();

        // ── متوسط نفس اليوم في آخر 3 شهور ──
        public decimal? HistoryAverageSameDay { get; init; }
        public int HistoryMonthsCounted { get; init; }
        public decimal? ChangeVsHistoryAverage { get; init; }

        // ── الإنتاج الحقيقي والهالك (من رحلة الإنتاج، مش الرقم اليدوي) ──
        public int TotalRealCompleted { get; init; }
        public int TotalScrap { get; init; }

        /// <summary>هالك ÷ (تام + هالك)</summary>
        public decimal? ScrapPercent { get; init; }

        /// <summary>اليدوي مقابل الحقيقي — موجب = اليدوي أكتر</summary>
        public decimal? ManualVsRealChange { get; init; }

        /// <summary>منتجات اليدوي فيها بعيد عن الحقيقي بأكتر من 15% — الأكبر فرقًا الأول</summary>
        public List<MonthlyPlanStatisticsMismatchDto> Mismatches { get; init; } = new();

        // ── إنتاجية العامل ──
        /// <summary>المحقق ÷ الحاضرين، على الأيام اللي فيها حضور وتسجيل — null مع الفلتر أو من غير حضور</summary>
        public decimal? OutputPerPresentWorker { get; init; }
        public decimal? AveragePresentWorkers { get; init; }

        /// <summary>false لما الفلتر شغال — الحضور مش متقسّم بعيلة</summary>
        public bool HasAttendanceData { get; init; }

        // ── الفلتر (بتتملى في الخدمة بعد Build) ──
        public List<string> AvailableFamilies { get; set; } = new();
        public List<Core.Enums.Material?> AvailableMaterials { get; set; } = new();
        public MonthlyPlanStatisticsFilter Filter { get; set; } = MonthlyPlanStatisticsFilter.None;

        public bool HasAnyData => TotalPlanned > 0 || TotalAchieved > 0 || PreviousSameDay > 0 || TotalRealCompleted > 0;
    }

    /// <summary>يوم واحد في رسم التقدّم</summary>
    public class MonthlyPlanStatisticsDayDto
    {
        public DateTime Date { get; init; }
        public bool IsWorkday { get; init; }

        /// <summary>بعد AsOfDate — لسه ماجاش، مفيش محقق</summary>
        public bool IsFuture { get; init; }

        public int Quantity { get; init; }

        /// <summary>المحقق التراكمي لحد اليوم ده — null للأيام اللي لسه ماجتش</summary>
        public int? Cumulative { get; init; }

        /// <summary>المفروض تراكميًا لحد اليوم ده (خطة × أيام الشغل لحد اليوم ÷ الكلي)</summary>
        public decimal PlanCumulative { get; init; }

        /// <summary>التوقّع التراكمي بالإيقاع الحالي — للأيام الجاية بس (وآخر يوم فعلي كنقطة بداية)</summary>
        public decimal? ForecastCumulative { get; init; }

        /// <summary>محقق الشهر اللي فات تراكميًا لنفس رقم اليوم — null لو الشهر اللي فات أقصر</summary>
        public int? PreviousCumulative { get; init; }

        /// <summary>عدد الحاضرين في اليوم ده — null للأيام الجاية أو مع الفلتر</summary>
        public int? PresentWorkers { get; init; }
    }

    /// <summary>متوسط الإنتاج لكل يوم من أيام الأسبوع (أيام شغل منقضية بس)</summary>
    public class MonthlyPlanStatisticsWeekdayDto
    {
        public DayOfWeek Day { get; init; }
        public int WorkdayCount { get; init; }
        public decimal Average { get; init; }
    }

    public class MonthlyPlanStatisticsFamilyDto
    {
        public string FamilyName { get; init; } = "";
        public int ProductCount { get; init; }
        public int Planned { get; init; }
        public int Achieved { get; init; }
        public decimal ProRatedPlan { get; init; }

        /// <summary>المحقق ÷ خطة العيلة كلها</summary>
        public decimal? CompletionPercent { get; init; }

        /// <summary>المحقق ÷ المفروض لحد النهارده — أساس الحالة</summary>
        public decimal? PacePercent { get; init; }

        public PlanPaceStatus Status { get; init; }
        public decimal? WeightGrams { get; init; }

        public int RealCompleted { get; init; }
        public int ScrapPieces { get; init; }
        public decimal? ScrapPercent { get; init; }
    }

    /// <summary>فلتر الإحصائيات — عيلة و/أو مادة. الاتنين null = كل المنتجات</summary>
    public record MonthlyPlanStatisticsFilter(string? FamilyName, Core.Enums.Material? Material, bool FilterByMaterial)
    {
        /// <summary>اسم المجموعة للمنتجات اللي مالهاش عيلة — نفس اللي الشاشات التانية بتكتبه</summary>
        public const string NoFamily = "بدون عيلة";

        public static MonthlyPlanStatisticsFilter None { get; } = new(null, null, false);

        public bool IsActive => FamilyName is not null || FilterByMaterial;

        public bool Matches(MonthlyPlanTrackingDto p) =>
            (FamilyName is null || (p.FamilyName ?? NoFamily) == FamilyName)
            && (!FilterByMaterial || p.Material == Material);
    }

    /// <summary>الإنتاج الحقيقي لمنتج (آخر مرحلة − هالكها) والهالك على كل المراحل — من ProductionChartService</summary>
    public class MonthlyPlanStatisticsRealDto
    {
        public static MonthlyPlanStatisticsRealDto Empty { get; } = new();

        public int RealCompleted { get; init; }
        public int ScrapPieces { get; init; }
    }

    /// <summary>منتج رقمه اليدوي بعيد عن الإنتاج الحقيقي</summary>
    public class MonthlyPlanStatisticsMismatchDto
    {
        public string ProductName { get; init; } = "";
        public int Manual { get; init; }
        public int Real { get; init; }
        public int Difference => Manual - Real;

        /// <summary>أكتر من 15% فرق من الأكبر فيهم — أو رقم موجود قدام صفر</summary>
        public bool IsSignificant
        {
            get
            {
                var larger = Math.Max(Math.Abs(Manual), Math.Abs(Real));
                return larger > 0 && Math.Abs(Difference) / (decimal)larger > Services.MonthlyPlanStatisticsMath.MismatchThreshold;
            }
        }
    }

    /// <summary>موجز الصبح — المنتجات المتأخرة النهارده</summary>
    public class MonthlyPlanTodayBriefingDto
    {
        public int BehindCount { get; init; }

        /// <summary>مجموع "المطلوب يوميًا" للمنتجات المتأخرة بس</summary>
        public int RequiredToday { get; init; }

        public List<string> TopProducts { get; init; } = new();
    }
}
