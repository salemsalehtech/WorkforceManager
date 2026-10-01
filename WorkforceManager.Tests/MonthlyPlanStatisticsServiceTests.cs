using System;
using System.Linq;
using System.Threading.Tasks;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تبويب الإحصائيات — كل إجمالي لازم يساوي مجموع صفوف التتبّع نفسها،
    /// والسلسلة اليومية لازم توصل لنفس المحقق. يوليو 2026: أول الشهر أربع،
    /// الجمعة 3/10/17/24/31 إجازة → 26 يوم شغل.
    /// </summary>
    public class MonthlyPlanStatisticsServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const int Year = 2026;
        private const int Month = 7;

        private static DateTime Day(int day) => new(Year, Month, day);

        private async Task SetEntryAsync(int productId, DateTime date, int quantity)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<MonthlyPlanTrackingService>(scope).SetDailyEntryAsync(productId, date, quantity);
        }

        private async Task SetCorrectionAsync(int productId, DateTime date, int quantity)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<MonthlyPlanTrackingService>(scope).SetCorrectionAsync(productId, date, quantity);
        }

        private async Task SetPlanAsync(int productId, int quantity)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(productId, Year, Month, quantity);
        }

        private Task<MonthlyPlanStatisticsDto> GetAsync(DateTime asOf) =>
            _db.InScopeAsync<MonthlyPlanStatisticsService, MonthlyPlanStatisticsDto>(s => s.GetAsync(Year, Month, asOf));

        [Fact]
        public async Task Totals_pace_and_forecast_follow_the_tracking_rows()
        {
            await SetPlanAsync(TestDatabase.ProductChainId, 2600);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(2), 200);
            await SetCorrectionAsync(TestDatabase.ProductChainId, Day(2), 10);

            var stats = await GetAsync(Day(2));

            Assert.Equal(26, stats.TotalWorkdays);
            Assert.Equal(2, stats.ElapsedWorkdays);
            Assert.Equal(2600, stats.TotalPlanned);
            Assert.Equal(310, stats.TotalAchieved);
            Assert.Equal(200m, stats.ProRatedPlan);
            Assert.Equal(1.55m, stats.PacePercent);
            Assert.Equal(110, stats.PaceGap);
            Assert.Equal(4030, stats.Forecast);
            Assert.Equal(4030 - 2600, stats.ForecastGap);
            Assert.Equal(155m, stats.AverageDaily);
            Assert.Equal(96, stats.RequiredDaily); // ceil((2600 − 310) ÷ 24)
            Assert.Equal(1, stats.AheadCount);
            Assert.Equal(0, stats.BehindCount);
        }

        [Fact]
        public async Task Daily_series_includes_corrections_and_stops_at_as_of_date()
        {
            await SetPlanAsync(TestDatabase.ProductChainId, 2600);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(2), 200);
            await SetCorrectionAsync(TestDatabase.ProductChainId, Day(2), 10);

            var stats = await GetAsync(Day(2));

            Assert.Equal(31, stats.Days.Count);
            Assert.Equal(210, stats.Days[1].Quantity);
            Assert.Equal(stats.TotalAchieved, stats.Days[1].Cumulative);
            Assert.True(stats.Days[2].IsFuture);
            Assert.Null(stats.Days[2].Cumulative);
            Assert.False(stats.Days[2].IsWorkday); // الجمعة 3
            Assert.Equal(2600m, stats.Days[^1].PlanCumulative);
            Assert.Equal(stats.Forecast, (int)Math.Round(stats.Days[^1].ForecastCumulative!.Value));

            Assert.Equal(Day(2), stats.BestDayDate);
            Assert.Equal(210, stats.BestDayQuantity);
            Assert.Equal(2, stats.RecordedWorkdays);
        }

        [Fact]
        public async Task Previous_month_line_matches_same_day_comparison()
        {
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 6, 1), 50);
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 6, 2), 70);
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 6, 20), 999); // بعد نفس اليوم — مايدخلش المقارنة
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(2), 200);

            var stats = await GetAsync(Day(2));

            Assert.Equal(120, stats.PreviousSameDay);
            Assert.Equal(120, stats.Days[1].PreviousCumulative);
            Assert.Equal(ReportBuilderService.PercentChange(300, 120), stats.ChangeVsPrevious);
            Assert.Equal(1119, stats.Days[19].PreviousCumulative);
        }

        [Fact]
        public async Task Untouched_products_are_excluded_and_holidays_are_not_workdays()
        {
            await SetPlanAsync(TestDatabase.ProductChainId, 260);
            await SetEntryAsync(TestDatabase.ProductRingId, Day(1), 40); // خارج الخطة
            using (var scope = _db.CreateScope())
                await _db.GetService<MonthlyPlanTrackingService>(scope).AddHolidayAsync(Day(4));

            var stats = await GetAsync(Day(6));

            Assert.Equal(
                new[] { TestDatabase.ProductChainId, TestDatabase.ProductRingId }.OrderBy(i => i),
                stats.Products.Select(p => p.ProductId).OrderBy(i => i));
            Assert.Equal(1, stats.OutsidePlanCount);
            Assert.Equal(25, stats.TotalWorkdays);
            Assert.False(stats.Days[3].IsWorkday);
            var family = Assert.Single(stats.Families);
            Assert.Equal(260, family.Planned);
            Assert.Equal(40, family.Achieved);
        }

        [Fact]
        public async Task Weekday_averages_cover_elapsed_workdays_only()
        {
            await SetEntryAsync(TestDatabase.ProductChainId, Day(4), 60);  // سبت
            await SetEntryAsync(TestDatabase.ProductChainId, Day(11), 100); // سبت

            var stats = await GetAsync(Day(11));

            Assert.Equal(6, stats.Weekdays.Count);
            Assert.Equal(DayOfWeek.Saturday, stats.Weekdays[0].Day);
            Assert.Equal(2, stats.Weekdays[0].WorkdayCount);
            Assert.Equal(80m, stats.Weekdays[0].Average);
            Assert.DoesNotContain(stats.Weekdays, w => w.Day == DayOfWeek.Friday);
        }

        // ═══════════ الفلتر، متوسط 3 شهور، الحقيقي/الهالك، إنتاجية العامل، موجز الصبح، التصدير ═══════════

        private async Task SetMaterialAsync(int productId, Material material)
        {
            using var scope = _db.CreateScope();
            var context = _db.GetService<AppDbContext>(scope);
            (await context.Products.FindAsync(productId))!.Material = material;
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task Material_filter_narrows_totals_series_and_keeps_options()
        {
            await SetMaterialAsync(TestDatabase.ProductChainId, Material.Copper);
            await SetMaterialAsync(TestDatabase.ProductRingId, Material.Zamak);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100);
            await SetEntryAsync(TestDatabase.ProductRingId, Day(1), 40);

            var filtered = await _db.InScopeAsync<MonthlyPlanStatisticsService, MonthlyPlanStatisticsDto>(s =>
                s.GetAsync(Year, Month, Day(2), new MonthlyPlanStatisticsFilter(null, Material.Copper, true)));

            Assert.Equal(100, filtered.TotalAchieved);
            Assert.Equal(100, filtered.Days[0].Quantity);
            Assert.Single(filtered.Products);
            Assert.Contains(Material.Zamak, filtered.AvailableMaterials);
            Assert.False(filtered.HasAttendanceData);
            Assert.Null(filtered.OutputPerPresentWorker);
        }

        [Fact]
        public async Task History_average_uses_only_months_with_entries()
        {
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 6, 2), 120);
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 5, 1), 80);
            await SetEntryAsync(TestDatabase.ProductChainId, new DateTime(2026, 5, 20), 999); // بعد نفس اليوم
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 150);

            var stats = await GetAsync(Day(2));

            Assert.Equal(2, stats.HistoryMonthsCounted); // أبريل فاضي — مايتحسبش صفر
            Assert.Equal(100m, stats.HistoryAverageSameDay);
            Assert.Equal(ReportBuilderService.PercentChange(150, 100m), stats.ChangeVsHistoryAverage);
        }

        [Fact]
        public async Task Real_production_scrap_and_mismatch_come_from_the_production_line()
        {
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 100, Day(1), confirmOverride: true);
            }
            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(TestDatabase.BagStage3Id, Day(1), 25, note: "رفض جودة");

            var stats = await GetAsync(Day(2));

            // التام = آخر مرحلة − هالكها، والهالك المعروض كله — نفس ProductionChartService
            Assert.Equal(75, stats.TotalRealCompleted);
            Assert.Equal(25, stats.TotalScrap);
            Assert.Equal(0.25m, stats.ScrapPercent);
            var mismatch = Assert.Single(stats.Mismatches); // يدوي 0 قدام حقيقي 75
            Assert.Equal(-75, mismatch.Difference);
            Assert.Equal(75, stats.RealByProduct[TestDatabase.ProductBagId].RealCompleted);
        }

        [Fact]
        public async Task Output_per_worker_divides_by_present_workers_on_recorded_days()
        {
            using (var scope = _db.CreateScope())
            {
                var context = _db.GetService<AppDbContext>(scope);
                context.Attendances.Add(new Attendance { WorkerId = TestDatabase.WorkerAhmedId, Date = Day(1), Status = AttendanceStatus.Present });
                context.Attendances.Add(new Attendance { WorkerId = TestDatabase.WorkerSaidId, Date = Day(1), Status = AttendanceStatus.Present });
                context.Attendances.Add(new Attendance { WorkerId = TestDatabase.WorkerAhmedId, Date = Day(2), Status = AttendanceStatus.Present });
                await context.SaveChangesAsync();
            }
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100); // يوم 2 فيه حضور بس مفيش تسجيل — مايتحسبش

            var stats = await GetAsync(Day(2));

            Assert.Equal(2, stats.Days[0].PresentWorkers);
            Assert.Equal(50m, stats.OutputPerPresentWorker);
            Assert.Equal(2m, stats.AveragePresentWorkers);
        }

        [Fact]
        public async Task Morning_briefing_lists_behind_products_and_required_today()
        {
            await SetPlanAsync(TestDatabase.ProductChainId, 2600);
            await SetPlanAsync(TestDatabase.ProductRingId, 260);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 10);
            await SetEntryAsync(TestDatabase.ProductRingId, Day(1), 50); // سابق

            var briefing = await _db.InScopeAsync<MonthlyPlanStatisticsService, MonthlyPlanTodayBriefingDto>(
                s => s.GetTodayBriefingAsync(Day(2)));

            Assert.Equal(1, briefing.BehindCount);
            Assert.Equal(new[] { "سلسلة" }, briefing.TopProducts);
            Assert.Equal(108, briefing.RequiredToday); // ceil((2600 − 10) ÷ 24)
        }

        [Fact]
        public async Task Excel_export_writes_four_sheets()
        {
            await SetPlanAsync(TestDatabase.ProductChainId, 2600);
            await SetEntryAsync(TestDatabase.ProductChainId, Day(1), 100);
            var stats = await GetAsync(Day(2));
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"stats-{Guid.NewGuid():N}.xlsx");

            try
            {
                new MonthlyPlanStatisticsExcelService().Export(stats, "يوليو 2026", path);

                using var workbook = new ClosedXML.Excel.XLWorkbook(path);
                Assert.Equal(new[] { "الملخص", "يوم بيوم", "العيلات", "المنتجات" }, workbook.Worksheets.Select(w => w.Name));
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
    }
}
