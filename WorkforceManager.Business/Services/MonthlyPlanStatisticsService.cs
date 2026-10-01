using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تبويب "الإحصائيات" في الخطة الشهرية — بيجمع صفوف
    /// MonthlyPlanTrackingService.GetTrackingAsync نفسها (نفس المحقق/الخطة
    /// بالظبط اللي تبويب التخطيط بيعرضه) زائد:
    ///   • سلسلة يوم بيوم من نفس جدول المحقق اليدوي (+ التصليحات)
    ///   • الإنتاج الحقيقي والهالك لكل منتج من ProductionChartService (نفس
    ///     تعريف "التام" في باقي البرنامج) — للمقارنة بالرقم اليدوي ونسبة الهالك
    ///   • عدد الحاضرين كل يوم (Attendance) — لإنتاجية العامل
    ///   • نفس اليوم في آخر 3 شهور — متوسط أثبت من شهر واحد
    /// والحساب كله في MonthlyPlanStatisticsMath (نقية، متختبرة من غير قاعدة بيانات).
    ///
    /// **الشهر كامل بس (subPeriodId = 0)**. الفلتر (عيلة/مادة) بيتطبّق على صفوف
    /// التتبّع وعلى كل السلاسل بنفس قايمة المنتجات، فكل رقم في الشاشة بيقول
    /// نفس الحاجة؛ إنتاجية العامل بس بتتشال مع الفلتر (الحضور مش متقسّم بعيلة).
    /// </summary>
    public class MonthlyPlanStatisticsService
    {
        /// <summary>كام شهر لورا في متوسط "نفس اليوم"</summary>
        public const int HistoryMonths = 3;

        private readonly AppDbContext _db;
        private readonly MonthlyPlanTrackingService _tracking;
        private readonly ProductionChartService _productionChart;

        public MonthlyPlanStatisticsService(AppDbContext db, MonthlyPlanTrackingService tracking, ProductionChartService productionChart)
        {
            _db = db;
            _tracking = tracking;
            _productionChart = productionChart;
        }

        public async Task<MonthlyPlanStatisticsDto> GetAsync(int year, int month, DateTime asOfDate, MonthlyPlanStatisticsFilter? filter = null)
        {
            filter ??= MonthlyPlanStatisticsFilter.None;

            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var effectiveAsOf = asOfDate.Date > monthEnd ? monthEnd : asOfDate.Date;

            var allTracking = await _tracking.GetTrackingAsync(year, month, effectiveAsOf);
            var tracking = allTracking.Where(filter.Matches).ToList();
            HashSet<int>? productIds = filter.IsActive ? tracking.Select(p => p.ProductId).ToHashSet() : null;

            var holidays = (await _db.MonthlyWorkCalendarHolidays
                    .Where(h => h.Date >= monthStart && h.Date <= monthEnd)
                    .Select(h => h.Date)
                    .ToListAsync())
                .Select(d => d.Date)
                .ToHashSet();

            // المحقق اليومي = الإدخال اليدوي + التصليحات — نفس تعريف EffectiveAchieved، بس مفرود على الأيام
            var current = SumByDate(await LoadEntriesAsync(monthStart, effectiveAsOf), productIds);
            foreach (var (date, quantity) in SumByDate(await LoadCorrectionsAsync(monthStart, effectiveAsOf), productIds))
                current[date] = current.GetValueOrDefault(date) + quantity;

            // آخر 3 شهور: إدخال يدوي بس، من غير تصليحات — نفس تعريف SameDayPreviousMonth
            // في التتبّع، عشان خط "الشهر اللي فات" في الرسم يقول نفس رقم جدول المقارنة
            var historyStart = monthStart.AddMonths(-HistoryMonths);
            var history = SumByDate(await LoadEntriesAsync(historyStart, monthStart.AddDays(-1)), productIds);
            var previousStart = monthStart.AddMonths(-1);
            var previous = history.Where(kv => kv.Key >= previousStart).ToDictionary(kv => kv.Key, kv => kv.Value);

            var sameDayHistory = new List<int?>();
            for (var back = 1; back <= HistoryMonths; back++)
            {
                var start = monthStart.AddMonths(-back);
                var end = start.AddMonths(1).AddDays(-1);
                var sameDay = new DateTime(start.Year, start.Month, Math.Min(effectiveAsOf.Day, DateTime.DaysInMonth(start.Year, start.Month)));
                var monthHasEntries = history.Any(kv => kv.Key >= start && kv.Key <= end);
                sameDayHistory.Add(monthHasEntries
                    ? history.Where(kv => kv.Key >= start && kv.Key <= sameDay).Sum(kv => kv.Value)
                    : null);
            }

            // الإنتاج الحقيقي (آخر مرحلة − هالكها) والهالك المعروض (كل المراحل) — نفس ProductionChartService بالحرف
            var realPoints = await _productionChart.GetProductOutputAsync(monthStart, effectiveAsOf, ChartGrain.Day);
            var real = realPoints
                .Where(p => p.BucketStart <= effectiveAsOf && (productIds is null || productIds.Contains(p.ProductId)))
                .GroupBy(p => p.ProductId)
                .ToDictionary(g => g.Key, g => new MonthlyPlanStatisticsRealDto
                {
                    RealCompleted = g.Sum(p => p.CompletedPieces),
                    ScrapPieces = g.Sum(p => p.ScrapPieces)
                });

            // الحاضرين كل يوم — مع الفلتر مالوش معنى (الحضور مش متقسّم بعيلة)
            Dictionary<DateTime, int>? present = null;
            if (!filter.IsActive)
                present = (await _db.Attendances
                        .Where(a => a.Date >= monthStart && a.Date <= effectiveAsOf && a.Status == AttendanceStatus.Present)
                        .GroupBy(a => a.Date)
                        .Select(g => new { Date = g.Key, Count = g.Count() })
                        .ToListAsync())
                    .GroupBy(x => x.Date.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            var stats = MonthlyPlanStatisticsMath.Build(year, month, effectiveAsOf, new MonthlyPlanStatisticsInputs
            {
                Tracking = tracking,
                DailyCurrent = current,
                DailyPrevious = previous,
                Holidays = holidays,
                SameDayHistory = sameDayHistory,
                RealByProduct = real,
                PresentByDate = present
            });

            stats.AvailableFamilies = allTracking.Select(p => p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily)
                .Distinct().OrderBy(n => n).ToList();
            stats.AvailableMaterials = allTracking.Select(p => p.Material).Distinct()
                .OrderBy(m => m is null ? 99 : (int)m.Value).ToList();
            stats.Filter = filter;
            return stats;
        }

        /// <summary>
        /// موجز الصبح: المنتجات المتأخرة في الشهر الحالي وكام قطعة مطلوبة منها
        /// النهارده. **استعلام التتبّع كله مرة واحدة** — بيتنده مرة عند فتح
        /// البرنامج، مش مع كل تنقّل (شوف ليه في RefreshNotificationBell).
        /// </summary>
        public async Task<MonthlyPlanTodayBriefingDto> GetTodayBriefingAsync(DateTime today)
        {
            var tracking = await _tracking.GetTrackingAsync(today.Year, today.Month, today);
            return MonthlyPlanStatisticsMath.BuildBriefing(tracking);
        }

        private async Task<List<(DateTime Date, int ProductId, int Quantity)>> LoadEntriesAsync(DateTime from, DateTime to) =>
            (await _db.MonthlyPlanDailyEntries
                .Where(e => e.Date >= from.Date && e.Date <= to.Date)
                .GroupBy(e => new { e.Date, e.ProductId })
                .Select(g => new { g.Key.Date, g.Key.ProductId, Total = g.Sum(e => e.Quantity) })
                .ToListAsync())
            .Select(x => (x.Date.Date, x.ProductId, x.Total))
            .ToList();

        private async Task<List<(DateTime Date, int ProductId, int Quantity)>> LoadCorrectionsAsync(DateTime from, DateTime to) =>
            (await _db.MonthlyPlanCorrections
                .Where(c => c.Date >= from.Date && c.Date <= to.Date)
                .GroupBy(c => new { c.Date, c.ProductId })
                .Select(g => new { g.Key.Date, g.Key.ProductId, Total = g.Sum(c => c.Quantity) })
                .ToListAsync())
            .Select(x => (x.Date.Date, x.ProductId, x.Total))
            .ToList();

        private static Dictionary<DateTime, int> SumByDate(
            IEnumerable<(DateTime Date, int ProductId, int Quantity)> rows, HashSet<int>? productIds) =>
            rows.Where(r => productIds is null || productIds.Contains(r.ProductId))
                .GroupBy(r => r.Date)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    /// <summary>كل اللي Build محتاجه — جاهز من الخدمة، فالحساب نفسه مفيهوش أي استعلام</summary>
    public class MonthlyPlanStatisticsInputs
    {
        public IReadOnlyList<MonthlyPlanTrackingDto> Tracking { get; init; } = Array.Empty<MonthlyPlanTrackingDto>();
        public IReadOnlyDictionary<DateTime, int> DailyCurrent { get; init; } = new Dictionary<DateTime, int>();
        public IReadOnlyDictionary<DateTime, int> DailyPrevious { get; init; } = new Dictionary<DateTime, int>();
        public IReadOnlySet<DateTime> Holidays { get; init; } = new HashSet<DateTime>();

        /// <summary>محقق نفس اليوم في كل شهر من الشهور اللي فاتت (الأقرب الأول) — null = الشهر ده مكانش فيه تسجيل خالص</summary>
        public IReadOnlyList<int?> SameDayHistory { get; init; } = Array.Empty<int?>();

        public IReadOnlyDictionary<int, MonthlyPlanStatisticsRealDto> RealByProduct { get; init; } = new Dictionary<int, MonthlyPlanStatisticsRealDto>();

        /// <summary>null = مش متاح (فلتر شغال)</summary>
        public IReadOnlyDictionary<DateTime, int>? PresentByDate { get; init; }
    }

    /// <summary>
    /// حساب الإحصائيات نفسه — نقي تمامًا. كل إجمالي هنا مجموع صفوف التتبّع
    /// (مش استعلام تاني)، فرقم "المحقق" هنا = مجموع اللي تبويب التخطيط بيعرضه بالظبط.
    /// </summary>
    public static class MonthlyPlanStatisticsMath
    {
        /// <summary>نفس هامش الإيقاع في MonthlyPlanTrackingService (أقل من 90% متأخر، أكتر من 110% سابق)</summary>
        private const decimal OnTrackLowerBound = 0.90m;
        private const decimal OnTrackUpperBound = 1.10m;

        /// <summary>فرق اليدوي عن الحقيقي اللي يستاهل يتعلّم عليه — نفس حد تحذير الاختلاف في تبويب الإنتاج اليومي</summary>
        public const decimal MismatchThreshold = 0.15m;

        public static PlanPaceStatus StatusOf(decimal? pacePercent) => pacePercent switch
        {
            null => PlanPaceStatus.OnTrack,
            < OnTrackLowerBound => PlanPaceStatus.Behind,
            > OnTrackUpperBound => PlanPaceStatus.Ahead,
            _ => PlanPaceStatus.OnTrack
        };

        /// <summary>هالك ÷ (تام + هالك) — null لو مفيش إنتاج حقيقي خالص</summary>
        public static decimal? ScrapPercent(int completed, int scrap) =>
            completed + scrap > 0 ? (decimal)scrap / (completed + scrap) : null;

        public static MonthlyPlanTodayBriefingDto BuildBriefing(IReadOnlyList<MonthlyPlanTrackingDto> tracking)
        {
            var behind = tracking
                .Where(p => p.PlannedQuantity > 0 && p.Status == PlanPaceStatus.Behind)
                .OrderBy(p => p.AchievedPercent ?? 0)
                .ToList();
            return new MonthlyPlanTodayBriefingDto
            {
                BehindCount = behind.Count,
                RequiredToday = behind.Sum(p => p.RequiredDailyOutput ?? 0),
                TopProducts = behind.Take(3).Select(p => p.ProductName).ToList()
            };
        }

        public static MonthlyPlanStatisticsDto Build(int year, int month, DateTime asOfDate, MonthlyPlanStatisticsInputs inputs)
        {
            var holidays = inputs.Holidays;
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var asOf = asOfDate.Date > monthEnd ? monthEnd : asOfDate.Date;

            var totalWorkdays = WorkCalendarRules.TotalWorkdays(monthStart, monthEnd, holidays);
            var elapsedWorkdays = WorkCalendarRules.ElapsedWorkdays(monthStart, monthEnd, asOf, holidays);
            var remainingWorkdays = totalWorkdays - elapsedWorkdays;

            var real = inputs.RealByProduct;
            var products = inputs.Tracking
                .Where(p => p.PlannedQuantity > 0 || p.EffectiveAchieved != 0 || (p.SameDayPreviousMonth ?? 0) > 0
                            || real.ContainsKey(p.ProductId))
                .ToList();

            var totalPlanned = products.Sum(p => p.PlannedQuantity);
            var totalAchieved = products.Sum(p => p.EffectiveAchieved);
            var proRated = products.Sum(p => p.ProRatedPlan);

            decimal? completion = totalPlanned > 0 ? (decimal)totalAchieved / totalPlanned : null;
            decimal? pace = proRated > 0 ? totalAchieved / proRated : null;
            int? forecast = elapsedWorkdays > 0
                ? (int)Math.Round(totalAchieved * (decimal)totalWorkdays / elapsedWorkdays)
                : null;
            decimal? averageDaily = elapsedWorkdays > 0 ? (decimal)totalAchieved / elapsedWorkdays : null;

            var previousSameDay = products.Sum(p => p.SameDayPreviousMonth ?? 0);

            // ── متوسط نفس اليوم في آخر 3 شهور (الشهور اللي فيها تسجيل بس) ──
            var historyMonths = inputs.SameDayHistory.Where(v => v is not null).Select(v => v!.Value).ToList();
            decimal? historyAverage = historyMonths.Count > 0 ? (decimal)historyMonths.Average() : null;

            // ── يوم بيوم ──
            var days = new List<MonthlyPlanStatisticsDayDto>();
            var cumulative = 0;
            var workdaysSoFar = 0;
            var futureWorkdays = 0;
            var previousCumulative = 0;
            var previousMonthStart = monthStart.AddMonths(-1);
            var previousDaysInMonth = DateTime.DaysInMonth(previousMonthStart.Year, previousMonthStart.Month);

            for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
            {
                var isWorkday = WorkCalendarRules.IsWorkday(day, holidays);
                if (isWorkday) workdaysSoFar++;
                var isFuture = day > asOf;

                var quantity = isFuture ? 0 : inputs.DailyCurrent.GetValueOrDefault(day);
                if (!isFuture) cumulative += quantity;

                decimal? forecastCumulative = null;
                if (averageDaily is { } avg)
                {
                    if (isFuture)
                    {
                        if (isWorkday) futureWorkdays++;
                        forecastCumulative = cumulative + avg * futureWorkdays;
                    }
                    else if (day == asOf && asOf < monthEnd)
                    {
                        forecastCumulative = cumulative; // نقطة بداية خط التوقّع
                    }
                }

                int? previousValue = null;
                if (day.Day <= previousDaysInMonth)
                {
                    previousCumulative += inputs.DailyPrevious.GetValueOrDefault(previousMonthStart.AddDays(day.Day - 1));
                    previousValue = previousCumulative;
                }

                days.Add(new MonthlyPlanStatisticsDayDto
                {
                    Date = day,
                    IsWorkday = isWorkday,
                    IsFuture = isFuture,
                    Quantity = quantity,
                    Cumulative = isFuture ? null : cumulative,
                    PlanCumulative = totalWorkdays == 0 ? 0 : (decimal)totalPlanned * workdaysSoFar / totalWorkdays,
                    ForecastCumulative = forecastCumulative,
                    PreviousCumulative = previousValue,
                    PresentWorkers = isFuture ? null : inputs.PresentByDate?.GetValueOrDefault(day)
                });
            }

            var elapsedWorkdayRows = days.Where(d => !d.IsFuture && d.IsWorkday).ToList();
            var bestDay = days.Where(d => !d.IsFuture && d.Quantity > 0)
                .OrderByDescending(d => d.Quantity).ThenBy(d => d.Date)
                .FirstOrDefault();

            // ── إنتاجية العامل: أيام فيها حضور وتسجيل بس (يوم من غير حضور متسجّل مايتحسبش صفر) ──
            var staffedDays = days.Where(d => d is { IsFuture: false, PresentWorkers: > 0 } && d.Quantity > 0).ToList();
            decimal? perWorker = staffedDays.Count > 0
                ? (decimal)staffedDays.Sum(d => d.Quantity) / staffedDays.Sum(d => d.PresentWorkers!.Value)
                : null;
            decimal? averagePresent = staffedDays.Count > 0 ? (decimal)staffedDays.Average(d => d.PresentWorkers!.Value) : null;

            // ترتيب أسبوع الشغل: السبت → الخميس (الجمعة إجازة دايمًا)
            var weekdayOrder = new[]
            {
                DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday,
                DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday
            };
            var weekdays = weekdayOrder
                .Select(dow =>
                {
                    var rows = elapsedWorkdayRows.Where(d => d.Date.DayOfWeek == dow).ToList();
                    return new MonthlyPlanStatisticsWeekdayDto
                    {
                        Day = dow,
                        WorkdayCount = rows.Count,
                        Average = rows.Count == 0 ? 0 : (decimal)rows.Sum(d => d.Quantity) / rows.Count
                    };
                })
                .ToList();

            MonthlyPlanStatisticsRealDto RealOf(int productId) =>
                real.TryGetValue(productId, out var r) ? r : MonthlyPlanStatisticsRealDto.Empty;

            // ── العيلات ──
            var families = products
                .GroupBy(p => p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily)
                .Select(g =>
                {
                    var planned = g.Sum(p => p.PlannedQuantity);
                    var achieved = g.Sum(p => p.EffectiveAchieved);
                    var familyProRated = g.Sum(p => p.ProRatedPlan);
                    decimal? familyPace = familyProRated > 0 ? achieved / familyProRated : null;
                    var weighed = g.Where(p => p.TotalWeightGrams is not null).ToList();
                    var completed = g.Sum(p => RealOf(p.ProductId).RealCompleted);
                    var scrap = g.Sum(p => RealOf(p.ProductId).ScrapPieces);
                    return new MonthlyPlanStatisticsFamilyDto
                    {
                        FamilyName = g.Key,
                        ProductCount = g.Count(),
                        Planned = planned,
                        Achieved = achieved,
                        ProRatedPlan = familyProRated,
                        CompletionPercent = planned > 0 ? (decimal)achieved / planned : null,
                        PacePercent = familyPace,
                        Status = StatusOf(familyPace),
                        WeightGrams = weighed.Count == 0 ? null : weighed.Sum(p => p.TotalWeightGrams!.Value),
                        RealCompleted = completed,
                        ScrapPieces = scrap,
                        ScrapPercent = ScrapPercent(completed, scrap)
                    };
                })
                .OrderByDescending(f => f.Planned).ThenBy(f => f.FamilyName)
                .ToList();

            var plannedProducts = products.Where(p => p.PlannedQuantity > 0).ToList();

            // ── اليدوي مقابل الحقيقي ──
            var totalReal = products.Sum(p => RealOf(p.ProductId).RealCompleted);
            var totalScrap = products.Sum(p => RealOf(p.ProductId).ScrapPieces);
            var mismatches = products
                .Select(p => new MonthlyPlanStatisticsMismatchDto
                {
                    ProductName = p.ProductName,
                    Manual = p.EffectiveAchieved,
                    Real = RealOf(p.ProductId).RealCompleted
                })
                .Where(m => m.IsSignificant)
                .OrderByDescending(m => Math.Abs(m.Difference))
                .ToList();

            return new MonthlyPlanStatisticsDto
            {
                Year = year,
                Month = month,
                AsOfDate = asOf,
                TotalWorkdays = totalWorkdays,
                ElapsedWorkdays = elapsedWorkdays,
                RemainingWorkdays = remainingWorkdays,
                TotalPlanned = totalPlanned,
                TotalAchieved = totalAchieved,
                ProRatedPlan = proRated,
                CompletionPercent = completion,
                PacePercent = pace,
                PaceGap = (int)Math.Round(totalAchieved - proRated),
                ExpectedPercent = totalWorkdays == 0 ? 0 : (decimal)elapsedWorkdays / totalWorkdays,
                Forecast = forecast,
                ForecastGap = forecast is { } f && totalPlanned > 0 ? f - totalPlanned : null,
                AverageDaily = averageDaily,
                RequiredDaily = totalPlanned > 0
                    ? MonthlyPlanFamilyMath.RequiredDailyOutput(totalPlanned, totalAchieved, remainingWorkdays)
                    : null,
                PreviousSameDay = previousSameDay,
                ChangeVsPrevious = ReportBuilderService.PercentChange(totalAchieved, previousSameDay),
                HistoryAverageSameDay = historyAverage,
                HistoryMonthsCounted = historyMonths.Count,
                ChangeVsHistoryAverage = ReportBuilderService.PercentChange(totalAchieved, historyAverage),
                TotalWeightGrams = products.Sum(p => p.TotalWeightGrams ?? 0),
                ProductsWithoutWeight = products.Count(p => p.EffectiveAchieved > 0 && p.PieceWeightGrams is null),
                AheadCount = plannedProducts.Count(p => p.Status == PlanPaceStatus.Ahead),
                OnTrackCount = plannedProducts.Count(p => p.Status == PlanPaceStatus.OnTrack),
                BehindCount = plannedProducts.Count(p => p.Status == PlanPaceStatus.Behind),
                OutsidePlanCount = products.Count(p => p.IsOutsidePlan),
                RecordedWorkdays = elapsedWorkdayRows.Count(d => d.Quantity != 0),
                BestDayDate = bestDay?.Date,
                BestDayQuantity = bestDay?.Quantity ?? 0,
                TotalRealCompleted = totalReal,
                TotalScrap = totalScrap,
                ScrapPercent = ScrapPercent(totalReal, totalScrap),
                ManualVsRealChange = ReportBuilderService.PercentChange(totalAchieved, totalReal),
                Mismatches = mismatches,
                OutputPerPresentWorker = perWorker,
                AveragePresentWorkers = averagePresent,
                HasAttendanceData = inputs.PresentByDate is not null,
                Days = days,
                Weekdays = weekdays,
                Families = families,
                Products = products,
                RealByProduct = products.ToDictionary(p => p.ProductId, p => RealOf(p.ProductId))
            };
        }
    }
}
