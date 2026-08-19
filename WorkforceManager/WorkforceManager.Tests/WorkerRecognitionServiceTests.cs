using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// WorkerRecognitionService.AwardTitlesForClosedPeriodsAsync — أهم
    /// ضمانتين هنا: أول تشغيل بعد التحديث ميرجعش يحسب تاريخ قديم (يبدأ من
    /// الفترة الحالية بس)، والتشغيلات اللي بعدها بتسجّل لقب لكل فترة قفلت
    /// فعلًا مرة واحدة بس.
    ///
    /// كل اختبار بيستخدم ملف settings.json مؤقت (settingsPath) — الخدمة دي
    /// أول خدمة Business بتلمس AppSettingsStore، فلازم تُعزل عن settings.json
    /// الحقيقي بتاع أي نسخة شغالة على نفس الجهاز.
    /// </summary>
    public class WorkerRecognitionServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly string _settingsPath =
            Path.Combine(Path.GetTempPath(), $"wm-recognition-settings-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            _db.Dispose();
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }

        private async Task RecordAsync(int stageId, int pieces, int workerId, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private Task AwardAsync() =>
            _db.InScopeAsync<WorkerRecognitionService, bool>(async service =>
            {
                await service.AwardTitlesForClosedPeriodsAsync(_settingsPath);
                return true;
            });

        private async Task<List<WorkerPerformanceTitle>> AllTitlesAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            return await db.WorkerPerformanceTitles.AsNoTracking().ToListAsync();
        }

        [Fact]
        public async Task FirstRun_SetsTheMarkersWithoutAwardingAnyPastPeriod()
        {
            await AwardAsync();

            Assert.Empty(await AllTitlesAsync());

            var settings = AppSettingsStore.Load(_settingsPath);
            Assert.NotNull(settings.LastBestWorkerWeekComputedFor);
            Assert.NotNull(settings.LastBestWorkerMonthComputedFor);
        }

        [Fact]
        public async Task SecondRun_AwardsExactlyTheOneClosedWeek()
        {
            var (currentWeekStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);
            var closedWeekDay = currentWeekStart.AddDays(-7);
            var currentMonthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            await RecordAsync(TestDatabase.RingStage1Id, 5000, TestDatabase.WorkerAhmedId, closedWeekDay);

            // المؤشر الأسبوعي واقف من أسبوعين (فيه أسبوع واحد قفل من غير
            // ما يتحسب)؛ الشهري متسجّل على الشهر الحالي عشان الاختبار
            // يفضل مركّز على الأسبوعي بس
            AppSettingsStore.Save(new AppSettings
            {
                LastBestWorkerWeekComputedFor = currentWeekStart.AddDays(-14),
                LastBestWorkerMonthComputedFor = currentMonthStart
            }, _settingsPath);

            await AwardAsync();

            var weekly = (await AllTitlesAsync())
                .Where(t => t.TitleType == PerformanceTitleType.WeeklyTop3).ToList();

            var winner = Assert.Single(weekly);
            Assert.Equal(TestDatabase.WorkerAhmedId, winner.WorkerId);
            Assert.Equal(closedWeekDay, winner.PeriodStart);

            // المؤشر بيقف على آخر أسبوع اتحسبله لقب فعلًا (مش على الأسبوع
            // الحالي نفسه — لسه ماقفلش)
            var settings = AppSettingsStore.Load(_settingsPath);
            Assert.Equal(closedWeekDay, settings.LastBestWorkerWeekComputedFor);
        }

        [Fact]
        public async Task RunningAgainRightAway_NeverDuplicatesATitle()
        {
            await AwardAsync();
            await AwardAsync();

            Assert.Empty(await AllTitlesAsync());
        }

        [Fact]
        public async Task GetCurrentTitleHoldersAsync_ReturnsOnlyTheLatestPeriodPerType()
        {
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.WorkerPerformanceTitles.AddRange(
                    new WorkerPerformanceTitle
                    {
                        WorkerId = TestDatabase.WorkerAhmedId, TitleType = PerformanceTitleType.WeeklyTop3,
                        PeriodStart = new DateTime(2026, 1, 1), PeriodEnd = new DateTime(2026, 1, 7)
                    },
                    new WorkerPerformanceTitle
                    {
                        WorkerId = TestDatabase.WorkerSaidId, TitleType = PerformanceTitleType.WeeklyTop3,
                        PeriodStart = new DateTime(2026, 1, 8), PeriodEnd = new DateTime(2026, 1, 14)
                    });
                await db.SaveChangesAsync();
            }

            var holders = await _db.InScopeAsync<WorkerRecognitionService, List<WorkerPerformanceTitle>>(
                service => service.GetCurrentTitleHoldersAsync());

            var winner = Assert.Single(holders);
            Assert.Equal(TestDatabase.WorkerSaidId, winner.WorkerId); // الأسبوع الأحدث بس
        }
    }
}
