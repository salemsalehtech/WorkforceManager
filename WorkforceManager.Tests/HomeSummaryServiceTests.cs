using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// HomeSummaryService بيجمّع من WeeklySummaryService/InitialBalanceService/
    /// ProductionMemoryService بس — مفيش رقم بيتحسب هنا من جديد، فالاختبارات
    /// دي بتتأكد من التجميع نفسه (فلترة الأرصدة القديمة، عدّ العمال
    /// النشطين) مش من قواعد أي خدمة تانية (دي متغطية في اختباراتها).
    /// </summary>
    public class HomeSummaryServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        private async Task RecordProductionAsync(int stageId, int pieces, int workerId, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private Task<HomeSummaryDto> GetSummaryAsync() =>
            _db.InScopeAsync<HomeSummaryService, HomeSummaryDto>(s => s.GetSummaryAsync(Today));

        [Fact]
        public async Task Fresh_install_with_no_data_returns_an_all_zero_summary_without_crashing()
        {
            var summary = await GetSummaryAsync();

            Assert.Equal(0, summary.TotalPiecesThisWeek);
            Assert.Equal(0, summary.ActiveWorkersThisWeek);
            Assert.Equal(0m, summary.NetWorkdaysThisWeek);
            Assert.Null(summary.BestWorkerOfWeek);
            Assert.Empty(summary.StaleInitialBalances);
            Assert.Equal(0, summary.DueMemoriesCount);
        }

        [Fact]
        public async Task Weekly_totals_match_the_exact_numbers_WeeklySummaryService_reports()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            // 200 قطعة ÷ 10 بالكوتة = 20 يومية لأحمد
            await RecordProductionAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);

            var team = await _db.InScopeAsync<WeeklySummaryService, List<WorkerWeeklySummaryDto>>(
                s => s.GetTeamWeeklySummaryAsync(weekStart));
            var expectedPieces = team.Sum(w => w.TotalPieces);
            var expectedNetWorkdays = team.Sum(w => w.NetWorkdays);

            var summary = await GetSummaryAsync();

            Assert.Equal(expectedPieces, summary.TotalPiecesThisWeek);
            Assert.Equal(1, summary.ActiveWorkersThisWeek);
            Assert.Equal(expectedNetWorkdays, summary.NetWorkdaysThisWeek);
        }

        [Fact]
        public async Task Best_worker_of_week_matches_rank_1_from_WeeklySummaryService()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            await RecordProductionAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.ChainStage1Id, 50, TestDatabase.WorkerSaidId, weekStart);

            var summary = await GetSummaryAsync();

            Assert.NotNull(summary.BestWorkerOfWeek);
            Assert.Equal(1, summary.BestWorkerOfWeek!.RecognitionRank);
            Assert.Equal(TestDatabase.WorkerAhmedId, summary.BestWorkerOfWeek!.WorkerId);
        }

        [Fact]
        public async Task An_initial_balance_older_than_the_threshold_is_flagged_as_stale()
        {
            var oldBalance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductRingId,
                    Name = "رصيد قديم",
                    Quantity = 10,
                    OriginalDate = Today.AddDays(-(HomeSummaryService.StaleInitialBalanceDays + 3))
                }));

            var summary = await GetSummaryAsync();

            Assert.Contains(summary.StaleInitialBalances, b => b.Id == oldBalance.Id);
        }

        [Fact]
        public async Task An_initial_balance_younger_than_the_threshold_is_not_flagged_as_stale()
        {
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductRingId,
                    Name = "رصيد جديد",
                    Quantity = 10,
                    OriginalDate = Today.AddDays(-2)
                }));

            var summary = await GetSummaryAsync();

            Assert.Empty(summary.StaleInitialBalances);
        }

        [Fact]
        public async Task A_completed_initial_balance_is_never_flagged_as_stale_regardless_of_age()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductRingId,
                    Name = "رصيد هيكتمل",
                    Quantity = 10,
                    OriginalDate = Today.AddDays(-30)
                }));

            // Status محسوبة من مجموع الاستخدامات مش عمود مخزّن (شوف InitialBalance.Status) —
            // أبسط طريقة تخلّي الرصيد Completed في اختبار هي صف استخدام مباشر بكمية = الإجمالي،
            // بدل تركيب رحلة إنتاج كاملة عبر WithdrawAsync اللي مش موضوع الاختبار ده
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.InitialBalanceUsages.Add(new InitialBalanceUsage
                {
                    InitialBalanceId = balance.Id,
                    Quantity = balance.Quantity,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    UsedDate = Today
                });
                await db.SaveChangesAsync();
            }

            var summary = await GetSummaryAsync();

            Assert.Empty(summary.StaleInitialBalances);
        }

        [Fact]
        public async Task A_due_memory_plan_is_counted()
        {
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<ProductionMemoryService>(scope).CreateAsync(
                    TestDatabase.ProductBagId,
                    new[] { TestDatabase.BagStage1Id },
                    "خطة مستحقة", Today);
            }

            var summary = await GetSummaryAsync();

            Assert.Equal(1, summary.DueMemoriesCount);
        }

        [Fact]
        public async Task Previous_week_total_pieces_reflects_the_week_before_not_the_current_one()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            await RecordProductionAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 50, TestDatabase.WorkerAhmedId, weekStart.AddDays(-7));

            var summary = await GetSummaryAsync();

            Assert.Equal(200, summary.TotalPiecesThisWeek);
            Assert.Equal(50, summary.PreviousWeekTotalPieces);
        }

        [Fact]
        public async Task Top_product_is_the_one_with_the_most_completed_pieces_this_week()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            await RecordProductionAsync(TestDatabase.RingStage1Id, 40, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.ChainStage1Id, 90, TestDatabase.WorkerSaidId, weekStart);

            var summary = await GetSummaryAsync();

            Assert.Equal("سلسلة", summary.TopProductName);
            Assert.Equal(90, summary.TopProductPieces);
        }

        [Fact]
        public async Task Attendance_rate_is_null_when_nobody_has_an_attendance_record_this_week()
        {
            var summary = await GetSummaryAsync();

            Assert.Null(summary.AttendanceRatePercent);
        }

        [Fact]
        public async Task Attendance_rate_matches_present_over_all_recorded_days()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            using (var scope = _db.CreateScope())
            {
                await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(weekStart, new[]
                {
                    (TestDatabase.WorkerAhmedId, AttendanceStatus.Present),
                    (TestDatabase.WorkerSaidId, AttendanceStatus.AbsentWithoutPermission)
                });
            }

            var summary = await GetSummaryAsync();

            // 1 حاضر من أصل 2 سجل = 50%
            Assert.Equal(50m, summary.AttendanceRatePercent);
        }
    }
}
