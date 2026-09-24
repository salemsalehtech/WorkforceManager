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
            Assert.Null(summary.WorstWorkerOfWeek);
            Assert.Null(summary.TopProduct);
            Assert.Null(summary.BottomProduct);
            Assert.Empty(summary.StaleInitialBalances);
            Assert.Empty(summary.DueSoonMemories);
            Assert.Equal(0, summary.UnexcusedAbsencesThisWeek);
            Assert.Equal(0, summary.StreakDays);
            Assert.False(summary.StreakIsCapped);
            Assert.Equal(0, summary.PreviousWeekActiveWorkers);
            Assert.Equal(0m, summary.PreviousWeekNetWorkdays);
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
        public async Task Memory_plans_due_within_the_window_are_listed_and_later_ones_are_not()
        {
            using (var scope = _db.CreateScope())
            {
                var memory = _db.GetService<ProductionMemoryService>(scope);
                await memory.CreateAsync(TestDatabase.ProductBagId, new[] { TestDatabase.BagStage1Id },
                    "خطة مستحقة", Today);
                await memory.CreateAsync(TestDatabase.ProductRingId, new[] { TestDatabase.RingStage1Id },
                    "خطة قريبة", Today.AddDays(HomeDashboardRules.MemoryDueSoonDays));
                await memory.CreateAsync(TestDatabase.ProductChainId, new[] { TestDatabase.ChainStage1Id },
                    "خطة بعيدة", Today.AddDays(HomeDashboardRules.MemoryDueSoonDays + 1));
            }

            var summary = await GetSummaryAsync();

            Assert.Equal(new[] { "خطة مستحقة", "خطة قريبة" }, summary.DueSoonMemories.Select(m => m.Notes));
        }

        [Fact]
        public async Task Previous_week_active_workers_and_net_workdays_come_from_the_week_before()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            await RecordProductionAsync(TestDatabase.RingStage1Id, 100, TestDatabase.WorkerAhmedId, weekStart.AddDays(-7));
            await RecordProductionAsync(TestDatabase.ChainStage1Id, 50, TestDatabase.WorkerSaidId, weekStart.AddDays(-7));

            var previousTeam = await _db.InScopeAsync<WeeklySummaryService, List<WorkerWeeklySummaryDto>>(
                s => s.GetTeamWeeklySummaryAsync(weekStart.AddDays(-7)));

            var summary = await GetSummaryAsync();

            Assert.Equal(2, summary.PreviousWeekActiveWorkers);
            Assert.Equal(previousTeam.Sum(w => w.NetWorkdays), summary.PreviousWeekNetWorkdays);
            Assert.Equal(0, summary.ActiveWorkersThisWeek);
        }

        [Fact]
        public async Task Unexcused_absences_match_the_weekly_summary_and_excused_ones_do_not_count()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            using (var scope = _db.CreateScope())
            {
                var attendance = _db.GetService<AttendanceService>(scope);
                await attendance.RecordAttendanceBatchAsync(weekStart, new[]
                {
                    (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission),
                    (TestDatabase.WorkerSaidId, AttendanceStatus.AbsentWithPermission)
                });
                await attendance.RecordAttendanceBatchAsync(weekStart.AddDays(1), new[]
                {
                    (TestDatabase.WorkerSaidId, AttendanceStatus.AbsentWithoutPermission)
                });
                await attendance.RecordAttendanceBatchAsync(weekStart.AddDays(-7), new[]
                {
                    (TestDatabase.WorkerSaidId, AttendanceStatus.AbsentWithoutPermission)
                });
            }

            var team = await _db.InScopeAsync<WeeklySummaryService, List<WorkerWeeklySummaryDto>>(
                s => s.GetTeamWeeklySummaryAsync(weekStart));

            var summary = await GetSummaryAsync();

            Assert.Equal(team.Sum(w => w.AbsentWithoutPermissionDays), summary.UnexcusedAbsencesThisWeek);
            Assert.Equal(2, summary.UnexcusedAbsencesThisWeek);
            Assert.Equal(1, summary.PreviousWeekUnexcusedAbsences);
        }

        [Fact]
        public async Task Streak_counts_clean_recorded_days_and_stops_at_the_last_unexcused_absence()
        {
            using (var scope = _db.CreateScope())
            {
                var attendance = _db.GetService<AttendanceService>(scope);
                // الاتنين 27 غياب بدون إذن ← الثلاثاء 28 والأربع 29 (النهارده) نضاف
                await attendance.RecordAttendanceBatchAsync(Today.AddDays(-2), new[]
                {
                    (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission)
                });
                foreach (var day in new[] { Today.AddDays(-1), Today })
                    await attendance.RecordAttendanceBatchAsync(day, new[]
                    {
                        (TestDatabase.WorkerAhmedId, AttendanceStatus.Present),
                        (TestDatabase.WorkerSaidId, AttendanceStatus.Present)
                    });
            }

            var summary = await GetSummaryAsync();

            Assert.Equal(2, summary.StreakDays);
            Assert.False(summary.StreakIsCapped);
        }

        [Fact]
        public async Task Top_and_bottom_products_carry_their_previous_week_pieces()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);

            await RecordProductionAsync(TestDatabase.ChainStage1Id, 90, TestDatabase.WorkerSaidId, weekStart);
            await RecordProductionAsync(TestDatabase.ChainStage1Id, 30, TestDatabase.WorkerSaidId, weekStart.AddDays(-7));
            // الدبلة مرحلتين — التام بيتحسب على آخر مرحلة (تلميع)
            await RecordProductionAsync(TestDatabase.RingStage1Id, 20, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage2Id, 20, TestDatabase.WorkerAhmedId, weekStart);

            var summary = await GetSummaryAsync();

            Assert.Equal(new HomeProductStat(TestDatabase.ProductChainId, "سلسلة", 90, 30), summary.TopProduct);
            Assert.Equal(new HomeProductStat(TestDatabase.ProductRingId, "دبلة", 20, 0), summary.BottomProduct);
        }

        [Fact]
        public async Task Bottom_product_is_null_when_only_one_product_worked_this_week()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);
            await RecordProductionAsync(TestDatabase.ChainStage1Id, 90, TestDatabase.WorkerSaidId, weekStart);

            var summary = await GetSummaryAsync();

            Assert.NotNull(summary.TopProduct);
            Assert.Null(summary.BottomProduct);
        }

        /// <summary>
        /// بيضيف عاملين إنتاج زيادة (مؤهلين على كل المراحل) — قاعدة الاختبار
        /// فيها عاملين إنتاج بس، و"الأقل أداءً" محتاج 4 في الترتيب.
        /// </summary>
        private async Task<(int Third, int Fourth)> AddTwoMoreProductionWorkersAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var third = new Worker { FullName = "كريم", IsActive = true, DailyWageEgp = 200m };
            var fourth = new Worker { FullName = "هاني", IsActive = true, DailyWageEgp = 200m };
            db.Workers.AddRange(third, fourth);
            await db.SaveChangesAsync();

            foreach (var workerId in new[] { third.Id, fourth.Id })
            foreach (var stageId in new[] { TestDatabase.RingStage1Id, TestDatabase.ChainStage1Id })
                db.WorkerSkills.Add(new WorkerSkill { WorkerId = workerId, ProductionStageId = stageId, Level = SkillLevel.Proficient });
            await db.SaveChangesAsync();

            return (third.Id, fourth.Id);
        }

        [Fact]
        public async Task Worst_worker_is_hidden_while_fewer_than_four_workers_are_ranked()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(Today);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 50, TestDatabase.WorkerSaidId, weekStart);

            var summary = await GetSummaryAsync();

            Assert.NotNull(summary.BestWorkerOfWeek);
            Assert.Null(summary.WorstWorkerOfWeek);
        }

        [Fact]
        public async Task Worst_worker_is_the_same_person_quick_search_names_for_worst_worker()
        {
            // البحث السريع بيحسب "الأسبوع ده" من ساعة الجهاز، فالاختبار ده بيشتغل
            // على أسبوع النهارده الحقيقي عشان الاتنين يقارنوا نفس الفترة
            var realToday = DateTime.Today;
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(realToday);
            var (third, fourth) = await AddTwoMoreProductionWorkersAsync();

            await RecordProductionAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 150, TestDatabase.WorkerSaidId, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 100, third, weekStart);
            await RecordProductionAsync(TestDatabase.RingStage1Id, 20, fourth, weekStart);
            // عامل ماأنتجش خالص (غياب بس) عمره مايتسمّى "الأقل أداءً"
            using (var scope = _db.CreateScope())
                await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(weekStart.AddDays(1), new[]
                {
                    (TestDatabase.WorkerMonaHourlyId, AttendanceStatus.AbsentWithoutPermission)
                });

            var summary = await _db.InScopeAsync<HomeSummaryService, HomeSummaryDto>(s => s.GetSummaryAsync(realToday));
            var searchAnswer = await _db.InScopeAsync<SearchIntentService, SearchIntentAnswer?>(s => s.AnswerAsync("اسوا عامل"));

            Assert.NotNull(summary.WorstWorkerOfWeek);
            Assert.Equal(fourth, summary.WorstWorkerOfWeek!.WorkerId);
            Assert.Equal(searchAnswer!.WorkerId, summary.WorstWorkerOfWeek.WorkerId);
            Assert.NotEqual(summary.BestWorkerOfWeek!.WorkerId, summary.WorstWorkerOfWeek.WorkerId);
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

            Assert.Equal("سلسلة", summary.TopProduct!.ProductName);
            Assert.Equal(90, summary.TopProduct.Pieces);
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
