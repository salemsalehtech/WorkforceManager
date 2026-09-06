using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// جولة QA شاملة على دورة الإنتاج اليومي + الرصيد الأولي وكل ما
    /// يتفرّع منها — مش اختبارات وحدة دقيقة زي باقي الملفات، الهدف
    /// تشغيل سيناريو واقعي متعدد الأيام والتأكد إن كل الخدمات المرتبطة
    /// (التقارير، الرسم البياني، الأجور، الأسبوعي) بتتفق مع بعض ومع
    /// اللي اتسجّل فعليًا. كل [Fact] بياخد TestDatabase نضيفة (نفس سلوك
    /// باقي ملفات الاختبار)، فمفيش اعتماد بين الحقائق.
    /// </summary>
    public class QaWorkflowScenarioTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => Day1.AddDays(1);
        private static DateTime Day3 => Day1.AddDays(2);
        private static DateTime Day4 => Day1.AddDays(3);

        [Fact]
        public async Task Day1_full_line_then_Day2_partial_line_report_correctly_and_double_assignment_is_blocked()
        {
            await _db.SignInTestUserAsync();

            // Day1: خط كامل (100 قطعة)، عاملين مختلفين على مراحل مختلفة
            using (var scope = _db.CreateScope())
            {
                var flow = _db.GetService<ProductionFlowService>(scope);
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 100 } };
                var shares = new[]
                {
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 },
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 100 },
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 }
                };
                var result = await flow.RecordFlowAsync(TestDatabase.ProductBagId, Day1, ranges, shares, confirmOverride: true);
                Assert.Empty(result.IncompleteRanges);
            }

            using (var checkScope = _db.CreateScope())
            {
                var dailyReport = await _db.GetService<DailyProductionReportService>(checkScope).GetAsync(Day1);
                var row = Assert.Single(dailyReport.Products);
                Assert.Equal(100, row.CompletedPieces);
                Assert.Equal(100, row.StartedPieces);

                var chartPoints = await _db.GetService<ProductionChartService>(checkScope)
                    .GetProductOutputAsync(Day1, Day1, ChartGrain.Day);
                Assert.Equal(100, Assert.Single(chartPoints).CompletedPieces);

                var general = await _db.GetService<ProductionReportService>(checkScope).GetGeneralReportAsync(Day1, Day1);
                Assert.Equal(100, general.TotalCompletedPieces);
                Assert.Equal(2, general.WorkersCount);
            }

            // Day2: نطاق يوقف عند مرحلة 2 (60 قطعة، عامل واحد) — مرحلة 3
            // صفر قطع النهارده، وده لازم يطلع فجوة 60 بس (مش 60+100)
            using (var scope = _db.CreateScope())
            {
                var flow = _db.GetService<ProductionFlowService>(scope);
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 60 } };
                var shares = new[]
                {
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 60 },
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 60 }
                };
                var result = await flow.RecordFlowAsync(TestDatabase.ProductBagId, Day2, ranges, shares, confirmOverride: true);
                Assert.Single(result.IncompleteRanges);
            }

            using (var checkScope = _db.CreateScope())
            {
                var dailyReport = await _db.GetService<DailyProductionReportService>(checkScope).GetAsync(Day2);
                var row = Assert.Single(dailyReport.Products);
                Assert.Equal(0, row.CompletedPieces);   // مفيش حاجة وصلت آخر مرحلة النهارده
                Assert.Equal(60, row.StartedPieces);

                var appDb = _db.GetService<AppDbContext>(checkScope);
                var autoBalance = await appDb.InitialBalances
                    .Include(b => b.Ranges)
                    .SingleAsync(b => b.ProductId == TestDatabase.ProductBagId && b.Source == InitialBalanceSource.DailyProduction);
                Assert.Equal(60, autoBalance.Quantity); // الفجوة الحقيقية بس
                var range = Assert.Single(autoBalance.Ranges);
                Assert.Equal(TestDatabase.BagStage3Id, range.FromStageId);
                Assert.Equal(TestDatabase.BagStage3Id, range.ToStageId);
            }

            // منع التكليف المزدوج: سعيد اتكلّف يوم2 على "شنطة" بالفعل —
            // تكليفه على "دبلة" في نفس اليوم لازم يترفض من غير تأكيد صريح
            using (var scope = _db.CreateScope())
            {
                var flow = _db.GetService<ProductionFlowService>(scope);
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.RingStage1Id, ToStageId = TestDatabase.RingStage1Id, PieceCount = 10 } };
                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.RingStage1Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 10 } };

                await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                    flow.RecordFlowAsync(TestDatabase.ProductRingId, Day2, ranges, shares, confirmOverride: false));
            }
        }

        /// <summary>
        /// منتج "دبلة" (نطاقين: تشكيل ثم تلميع) بترتيب متناقص (80 ثم 50) —
        /// نفس شكل الباگ التاريخي (5000/4500/4000) بس على منتج بمرحلتين،
        /// معزول عن سيناريو "شنطة" فوق عشان الحساب يبقى واضح.
        /// </summary>
        [Fact]
        public async Task Decreasing_multi_range_save_creates_only_the_real_gap_not_the_full_upstream_amount()
        {
            using var scope = _db.CreateScope();
            var flow = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.RingStage1Id, ToStageId = TestDatabase.RingStage1Id, PieceCount = 80 },
                new FlowRangeDto { FromStageId = TestDatabase.RingStage2Id, ToStageId = TestDatabase.RingStage2Id, PieceCount = 50 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.RingStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 80 },
                new FlowShareDto { ProductionStageId = TestDatabase.RingStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 50 }
            };

            var result = await flow.RecordFlowAsync(TestDatabase.ProductRingId, Day1, ranges, shares, confirmOverride: true);
            Assert.Single(result.IncompleteRanges);

            var appDb = _db.GetService<AppDbContext>(scope);
            var balance = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .SingleAsync(b => b.ProductId == TestDatabase.ProductRingId);

            Assert.Equal(30, balance.Quantity); // 80 - 50، مش 80 كاملة
            Assert.Equal(TestDatabase.RingStage2Id, Assert.Single(balance.Ranges).FromStageId);

            var dailyReport = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day1);
            var row = Assert.Single(dailyReport.Products);
            Assert.Equal(50, row.CompletedPieces); // آخر مرحلة (تلميع) = 50
            Assert.Equal(80, row.StartedPieces);   // أول مرحلة (تشكيل) = 80
        }

        [Fact]
        public async Task Multi_day_initial_balance_lifecycle_reports_the_gap_correctly_at_every_step()
        {
            await _db.SignInTestUserAsync();

            // رصيد بنطاقات (50) + رصيد بجزء متروك عن قصد (40 كلي، 30 في نطاق)
            InitialBalanceDto ranged;
            InitialBalanceRangeDto range;
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                ranged = await service.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "QA ranged",
                    Quantity = 50,
                    OriginalDate = Day1,
                    Ranges = new List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 50 }
                    }
                });
                range = Assert.Single(ranged.Ranges);

                var leftover = await service.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "QA leftover",
                    Quantity = 40,
                    OriginalDate = Day1,
                    Ranges = new List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 30 }
                    }
                });
                Assert.Equal(10, leftover.UnrangedQuantity);
            }

            // Day2: سحب جزئي (20 من 50) من النطاق
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
                await service.WithdrawAsync(ranged.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, Day2, confirmOverride: true);
            }

            using (var checkScope = _db.CreateScope())
            {
                var updated = await _db.GetService<InitialBalanceService>(checkScope).GetByIdAsync(ranged.Id);
                Assert.NotNull(updated);
                Assert.Equal(30, updated!.RemainingQuantity);
                Assert.Equal(InitialBalanceStatus.PartiallyUsed, updated.Status);

                // الفجوة (20 اللي اتسحبت) هي اللي بتظهر تام يوم2 — مش الـ50 كاملة
                var dailyReport = await _db.GetService<DailyProductionReportService>(checkScope).GetAsync(Day2);
                var row = Assert.Single(dailyReport.Products);
                Assert.Equal(20, row.CompletedPieces);
            }

            // Day4: سحب باقي الرصيد كله دفعة واحدة (سحب الكل)
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 30 } };
                await service.WithdrawAsync(ranged.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 30 } },
                    shares, Day4, confirmOverride: true);
            }

            using var finalScope = _db.CreateScope();
            var finalService = _db.GetService<InitialBalanceService>(finalScope);
            var finalState = await finalService.GetByIdAsync(ranged.Id);
            Assert.NotNull(finalState);
            Assert.Equal(0, finalState!.RemainingQuantity);
            Assert.Equal(InitialBalanceStatus.Completed, finalState.Status);

            // كمّل بالكامل → لازم يختفي من النشط ويظهر في السجل
            var active = await finalService.GetForProductAsync(TestDatabase.ProductBagId);
            Assert.DoesNotContain(active, b => b.Id == ranged.Id);
            var history = await finalService.GetHistoryForProductAsync(TestDatabase.ProductBagId);
            Assert.Contains(history, b => b.Id == ranged.Id);

            var day4Report = await _db.GetService<DailyProductionReportService>(finalScope).GetAsync(Day4);
            Assert.Equal(30, Assert.Single(day4Report.Products).CompletedPieces);
        }

        [Fact]
        public async Task Editing_a_partially_withdrawn_balance_locks_the_used_range_and_deleting_follows_hard_vs_soft_rule()
        {
            await _db.SignInTestUserAsync();

            InitialBalanceDto balance;
            InitialBalanceRangeDto range;
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                balance = await service.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "QA edit target",
                    Quantity = 50,
                    OriginalDate = Day1,
                    Ranges = new List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 50 }
                    }
                });
                range = Assert.Single(balance.Ranges);

                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 15 } };
                await service.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 15 } },
                    shares, Day2, confirmOverride: true);
            }

            // تعديل: تصغير النطاق تحت أرضية المستخدم (15) لازم يترفض
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.EditAsync(balance.Id, balance.Name, null, new List<InitialBalanceRangeEditItem>
                    {
                        new() { Id = range.Id, FromStageId = range.FromStageId, ToStageId = range.ToStageId, PieceCount = 10 }
                    }));
                Assert.Contains("أقل من الكمية المستخدمة", ex.Message);

                // تصغير لحد الأرضية بالظبط (15) ينجح
                var edited = await service.EditAsync(balance.Id, "QA edited name", "ملاحظة", new List<InitialBalanceRangeEditItem>
                {
                    new() { Id = range.Id, FromStageId = range.FromStageId, ToStageId = range.ToStageId, PieceCount = 15 }
                });
                Assert.Equal("QA edited name", edited.Name);
                // balance.Quantity (50) يفضل زي ما هو — تصغير النطاق لحد
                // 15 معناه 35 من الـ50 بقوا Unranged (مش محدد لهم نطاق)،
                // مش إن الرصيد نفسه بقى 15. RemainingQuantity = 50 - 15 = 35
                Assert.Equal(35, edited.RemainingQuantity);
                Assert.Equal(35, edited.UnrangedQuantity);
            }

            // حذف بصفر استخدام = نهائي
            int zeroUsageId;
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                var zeroUsage = await service.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "QA zero usage",
                    Quantity = 20,
                    OriginalDate = Day1
                });
                zeroUsageId = zeroUsage.Id;
                await service.DeleteAsync(zeroUsageId, null, "اتضاف بالغلط");
            }

            using (var checkScope = _db.CreateScope())
            {
                var appDb = _db.GetService<AppDbContext>(checkScope);
                Assert.False(await appDb.InitialBalances.IgnoreQueryFilters().AnyAsync(b => b.Id == zeroUsageId));
            }

            // حذف الرصيد اللي اتعدّل واستُخدم منه جزء (15) = ناعم، والأجر الحقيقي يفضل زي ما هو
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                await service.DeleteAsync(balance.Id, null, "اتقفل يدويًا");
            }

            using var finalScope = _db.CreateScope();
            var finalDb = _db.GetService<AppDbContext>(finalScope);
            var softDeleted = await finalDb.InitialBalances.IgnoreQueryFilters().SingleAsync(b => b.Id == balance.Id);
            Assert.True(softDeleted.IsDeleted);
            Assert.True(await finalDb.DailyProductions.AnyAsync(dp =>
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage3Id && dp.Date == Day2));
        }

        [Fact]
        public async Task Scrapping_a_balance_range_has_zero_wage_impact_and_hourly_work_never_touches_initial_balance_or_the_assignment_guard()
        {
            await _db.SignInTestUserAsync();

            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<InitialBalanceService>(scope);
                var balance = await service.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "QA scrap target",
                    Quantity = 25,
                    OriginalDate = Day1,
                    Ranges = new List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 25 }
                    }
                });
                var range = Assert.Single(balance.Ranges);

                var scrap = await service.WithdrawToScrapAsync(
                    balance.Id, range.Id, TestDatabase.BagStage3Id, Day2, 25, null, "رفض جودة", "");
                Assert.Equal(25, scrap.PieceCount);
            }

            using (var checkScope = _db.CreateScope())
            {
                var appDb = _db.GetService<AppDbContext>(checkScope);
                // مفيش أجر اتسجّل من السحب لهالك خالص
                Assert.False(await appDb.DailyProductions.AnyAsync(dp => dp.Date == Day2));

                // الهالك بيظهر كنشاط في تقرير اليوم عن قصد ("كام راح هالك؟")
                // حتى من غير تام أو داخل — HasActivity بتاعته بتحسب الهالك
                var dailyReport = await _db.GetService<DailyProductionReportService>(checkScope).GetAsync(Day2);
                var row = Assert.Single(dailyReport.Products);
                Assert.Equal(0, row.CompletedPieces);
                Assert.Equal(0, row.StartedPieces);
                Assert.Equal(25, row.ScrapPieces);
            }

            // شغل بالساعة: مفيش علاقة بالرصيد الأولي ولا بضابط التكليف المزدوج
            using (var scope = _db.CreateScope())
            {
                var hourly = _db.GetService<HourlyWorkdayService>(scope);
                var endHour = HourlyWorkdayService.ShiftPresets[0].EndHour24;
                var log = await hourly.RecordHourlyWorkAsync(TestDatabase.WorkerMonaHourlyId, Day2, endHour);
                Assert.True(log.WorkdaysCredited > 0);
            }

            using var finalScope = _db.CreateScope();

            // الرصيد كمّل بالكامل (هالك = استهلاك نهائي) رغم إن مفيش أجر اتسجّل
            var scrappedBalance = await _db.GetService<InitialBalanceService>(finalScope)
                .GetByIdAsync((await _db.GetService<AppDbContext>(finalScope).InitialBalances
                    .SingleAsync(b => b.Name == "QA scrap target")).Id);
            Assert.NotNull(scrappedBalance);
            Assert.Equal(InitialBalanceStatus.Completed, scrappedBalance!.Status);

            var finalDb = _db.GetService<AppDbContext>(finalScope);
            Assert.True(await finalDb.HourlyWorkLogs.AnyAsync(h => h.WorkerId == TestDatabase.WorkerMonaHourlyId && h.Date == Day2));
        }

        [Fact]
        public async Task Attendance_penalties_advances_corrections_and_weekly_payroll_stay_consistent_with_raw_entries()
        {
            await _db.SignInTestUserAsync();

            // Day1: إنتاج عادي لعاملين — الحضور المفروض يتسجّل تلقائي
            int recordToCorrectId;
            using (var scope = _db.CreateScope())
            {
                var flow = _db.GetService<ProductionFlowService>(scope);
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 100 } };
                var shares = new[]
                {
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 },
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 100 },
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 }
                };
                await flow.RecordFlowAsync(TestDatabase.ProductBagId, Day1, ranges, shares, confirmOverride: true);

                var appDb = _db.GetService<AppDbContext>(scope);
                Assert.True(await appDb.Attendances.AnyAsync(a => a.WorkerId == TestDatabase.WorkerAhmedId && a.Date == Day1 && a.Status == AttendanceStatus.Present));
                Assert.True(await appDb.Attendances.AnyAsync(a => a.WorkerId == TestDatabase.WorkerSaidId && a.Date == Day1 && a.Status == AttendanceStatus.Present));

                recordToCorrectId = await appDb.DailyProductions
                    .Where(dp => dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage1Id && dp.Date == Day1)
                    .Select(dp => dp.Id)
                    .SingleAsync();
            }

            // جزاء + سلفة/حافز على أحمد
            using (var scope = _db.CreateScope())
            {
                var penalties = _db.GetService<PenaltyService>(scope);
                await penalties.RecordPenaltyAsync(TestDatabase.WorkerAhmedId, Day1, "تأخير", PenaltyDeduction.HalfDay);

                var adjustments = _db.GetService<WageAdjustmentService>(scope);
                await adjustments.RecordAdjustmentAsync(TestDatabase.WorkerAhmedId, Day1, WageAdjustmentType.Bonus, 100m, "حافز إنتاج");
            }

            // تصحيح: تعديل عدد قطع سجل أحمد على أول مرحلة من 100 لـ 80
            using (var scope = _db.CreateScope())
            {
                var workday = _db.GetService<WorkdayCalculationService>(scope);
                await workday.UpdateProductionAsync(recordToCorrectId, 80, confirmOverride: true);
            }

            using var checkScope = _db.CreateScope();

            // التقرير العام والأسبوعي والأجر لازم يتفقوا على نفس أرقام أحمد
            var general = await _db.GetService<ProductionReportService>(checkScope).GetGeneralReportAsync(Day1, Day1);
            var ahmedGeneral = general.ByWorker.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);
            Assert.Equal(180, ahmedGeneral.TotalPieces); // 80 (بعد التصحيح) + 100 على آخر مرحلة

            var weekly = await _db.GetService<WeeklySummaryService>(checkScope)
                .GetTeamSummaryForRangeAsync(Day1, Day1);
            var ahmedWeekly = weekly.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);
            Assert.Equal(180, ahmedWeekly.TotalPieces);
            Assert.True(ahmedWeekly.PenaltyDeduction > 0);

            var payroll = await _db.GetService<PayrollService>(checkScope).GetPeriodPayrollAsync(Day1, Day1);
            var ahmedPayroll = payroll.Workers.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);
            Assert.Equal(180, ahmedPayroll.TotalPieces);
            Assert.Equal(100, ahmedPayroll.BonusEgp);
            Assert.Equal(ahmedWeekly.NetWorkdays, ahmedPayroll.NetWorkdays);
        }
    }
}
