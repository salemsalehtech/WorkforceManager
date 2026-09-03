using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    public class InitialBalanceServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;
        private static DateTime CompletionDay => Day.AddDays(2); // day to complete the balance

        // Helper to record base production
        private async Task RecordBaseAsync(int stageId, int pieces, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, stageId, pieces, date, confirmOverride: true);
        }

        // Helper to verify stage totals
        private async Task<int> GetStageTotalAsync(int stageId, DateTime date)
        {
            var totals = await _db.InScopeAsync<ProductionStageOutputService, System.Collections.Generic.IReadOnlyDictionary<int, int>>(s =>
                s.GetStageTotalsUpToAsync(date));
            return totals.TryGetValue(stageId, out var count) ? count : 0;
        }

        [Fact]
        public async Task CreateManual_ThenAddRanges_ValidationSucceeds()
        {
            // Create a balance of 100 pieces at stage 1
            var createReq = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "Balance A",
                Quantity = 100,
                OriginalDate = Day,
            };

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(createReq));

            Assert.Equal(100, balance.Quantity);
            Assert.Equal(InitialBalanceStatus.Available, balance.Status);

            // Add ranges
            var rangeDto = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage1Id,
                    ToStageId = TestDatabase.BagStage2Id,
                    PieceCount = 60
                }));

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Single(updated.Ranges);
            Assert.Equal(60, updated.Ranges.First().PieceCount);

            // Add exceeding range - should fail
            await Assert.ThrowsAnyAsync<Exception>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                    s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                    {
                        FromStageId = TestDatabase.BagStage2Id,
                        ToStageId = TestDatabase.BagStage3Id,
                        PieceCount = 50 // 60 + 50 = 110 > 100
                    })));
        }

        [Fact]
        public async Task Creating_a_balance_does_not_require_ranges_to_sum_to_total_quantity()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Partially distributed",
                    Quantity = 100,
                    OriginalDate = Day,
                    Ranges = new System.Collections.Generic.List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 60 }
                    }
                }));

            Assert.Equal(100, balance.Quantity);
            Assert.Single(balance.Ranges);
            Assert.Equal(40, balance.UnrangedQuantity);
        }

        // ======================= GetProductSummaryAsync =======================

        [Fact]
        public async Task Product_summary_for_a_product_with_no_balances_returns_zeros()
        {
            var summary = await _db.InScopeAsync<InitialBalanceService, InitialBalanceSummaryDto>(s =>
                s.GetProductSummaryAsync(TestDatabase.ProductBagId));

            Assert.Equal(0, summary.TotalQuantity);
            Assert.Equal(0, summary.UsedQuantity);
            Assert.Equal(0, summary.RemainingQuantity);
            Assert.Equal(0, summary.ActiveBalanceCount);
        }

        [Fact]
        public async Task Product_summary_sums_remaining_quantity_across_all_active_balances_for_the_product()
        {
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Balance X",
                    Quantity = 30,
                    OriginalDate = Day
                }));
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Balance Y",
                    Quantity = 20,
                    OriginalDate = Day
                }));

            var summary = await _db.InScopeAsync<InitialBalanceService, InitialBalanceSummaryDto>(s =>
                s.GetProductSummaryAsync(TestDatabase.ProductBagId));

            Assert.Equal(50, summary.TotalQuantity);
            Assert.Equal(0, summary.UsedQuantity);
            Assert.Equal(50, summary.RemainingQuantity);
            Assert.Equal(2, summary.ActiveBalanceCount);
        }

        [Fact]
        public async Task Product_summary_excludes_soft_deleted_balances()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Balance to delete",
                    Quantity = 30,
                    OriginalDate = Day
                }));

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).DeleteAsync(balance.Id, null, "اتضاف بالغلط");

            var summary = await _db.InScopeAsync<InitialBalanceService, InitialBalanceSummaryDto>(s =>
                s.GetProductSummaryAsync(TestDatabase.ProductBagId));

            Assert.Equal(0, summary.TotalQuantity);
            Assert.Equal(0, summary.ActiveBalanceCount);
        }

        [Fact]
        public async Task Withdrawing_records_wage_and_actual_output_on_the_completion_date_not_the_original_date()
        {
            // Arrange: sign in test user for operations password bypass (if any)
            await _db.SignInTestUserAsync();

            var createReq = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "Balance B",
                Quantity = 50,
                OriginalDate = Day,
            };

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s => s.CreateAsync(createReq));

            var rangeDto = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 50
                }));

            // baseline stage 3 totals before completion
            var baseS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, CompletionDay);
            Assert.Equal(0, baseS3);

            // Act: withdraw the whole range on CompletionDay — the range covers
            // BagStage2Id and BagStage3Id, so both need a worker share
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 }
            };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(
                    balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = rangeDto.Id, PieceCount = 50 } },
                    shares, CompletionDay, confirmOverride: true));

            // Assert: Balance updated (once, not doubled by the intermediate BagStage2Id row)
            var updatedNullable = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updatedNullable);
            var updated = updatedNullable;
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
            Assert.Equal(50, updated.UsedQuantity);

            // Assert: wage created on CompletionDay, as a fully normal production
            // row — WithdrawAsync goes through RecordFlowAsync like any other
            // flow entry, so IsBalanceCompletion is NOT set on it anymore
            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            var wage = await appDb.DailyProductions.FirstOrDefaultAsync(dp =>
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage3Id && dp.Date == CompletionDay);

            Assert.NotNull(wage);
            Assert.False(wage.IsBalanceCompletion);
            Assert.Equal(50, wage.PieceCount);

            // Assert: actual output now lands on the COMPLETION date, not the
            // balance's original date — this balance was created directly
            // (not via an incomplete flow), so the original date never had any
            // output on this stage to begin with
            var origDateS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, Day);
            Assert.Equal(0, origDateS3);

            var compDateS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, CompletionDay);
            Assert.Equal(50, compDateS3);
        }

        // ======================= WithdrawAsync =======================

        private async Task<(InitialBalanceDto Balance, InitialBalanceRangeDto Range)> CreateSingleStageBalanceAsync(int pieces, DateTime originalDate)
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Withdraw test balance",
                    Quantity = pieces,
                    OriginalDate = originalDate
                }));

            var range = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage3Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = pieces
                }));

            return (balance, range);
        }

        [Fact]
        public async Task Withdrawing_before_the_balances_original_date_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day.AddDays(10));

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                    s.WithdrawAsync(balance.Id,
                        new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                        shares, Day, confirmOverride: true))); // Day is 10 days before the balance's OriginalDate

            Assert.Contains("تاريخ السحب", ex.Message);
        }

        [Fact]
        public async Task Withdrawing_more_than_a_ranges_remaining_quantity_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 } };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                    s.WithdrawAsync(balance.Id,
                        new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 30 } },
                        shares, CompletionDay, confirmOverride: true)));

            Assert.Contains("أكبر من المتاح", ex.Message);
        }

        [Fact]
        public async Task Withdrawing_creates_real_daily_production_rows_via_the_flow_service()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };

            var result = await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            Assert.Single(result.CreatedRows);
            Assert.Equal(20, result.CreatedRows[0].PieceCount);

            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            Assert.True(await appDb.DailyProductions.AnyAsync(dp =>
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage3Id && dp.Date == CompletionDay));

            // حضور تلقائي زي أي رحلة عادية
            Assert.Equal(1, result.AttendanceMarkedCount);
        }

        [Fact]
        public async Task Withdrawing_the_whole_balance_marks_it_completed()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
            Assert.Equal(0, updated.RemainingQuantity);
        }

        [Fact]
        public async Task Withdrawing_applies_the_worker_assignment_guard()
        {
            await _db.SignInTestUserAsync();

            // أحمد مكلّف على منتج/مرحلة تانية في نفس اليوم بالفعل
            await RecordBaseAsync(TestDatabase.ThirdsStage1Id, 5, CompletionDay);

            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);
            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };

            // من غير تأكيد: لازم يترفض بطلب تأكيد صريح
            await Assert.ThrowsAsync<AssignmentConfirmationRequiredException>(() =>
                _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                    s.WithdrawAsync(balance.Id,
                        new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                        shares, CompletionDay, confirmOverride: false)));

            // بتأكيد صريح: تنجح عادي
            var result = await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            Assert.Single(result.CreatedRows);
        }

        [Fact]
        public async Task Withdrawing_from_a_balance_into_a_range_that_still_doesnt_reach_the_last_stage_creates_a_successor_balance()
        {
            await _db.SignInTestUserAsync();

            // رصيد نطاقه BagStage1Id فقط (مش آخر مرحلة) — سحبه هيتحول تلقائيًا لرصيد خلَف
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Chained balance",
                    Quantity = 20,
                    OriginalDate = Day
                }));

            var range = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage1Id,
                    ToStageId = TestDatabase.BagStage1Id,
                    PieceCount = 20
                }));

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            // الرصيد الأصلي اكتمل، وطلع رصيد جديد (خلَف) من BagStage2Id لـBagStage3Id
            var successor = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .SingleAsync(b => b.Id != balance.Id && b.ProductId == TestDatabase.ProductBagId);

            Assert.Equal(20, successor.Quantity);
            var successorRange = Assert.Single(successor.Ranges);
            Assert.Equal(TestDatabase.BagStage2Id, successorRange.FromStageId);
            Assert.Equal(TestDatabase.BagStage3Id, successorRange.ToStageId);
        }

        // ======================= WithdrawToScrapAsync =======================

        [Fact]
        public async Task Withdrawing_to_scrap_creates_a_scrap_row_and_a_usage_row_with_no_worker()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var scrap = await _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage3Id, CompletionDay, 20, null, "رفض جودة", ""));

            Assert.Equal(20, scrap.PieceCount);

            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            var usage = await appDb.InitialBalanceUsages.SingleAsync(u => u.InitialBalanceId == balance.Id);
            Assert.Null(usage.WorkerId);
            Assert.Null(usage.DailyProductionId);
            Assert.Equal(scrap.Id, usage.ProductionScrapId);

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
        }

        [Fact]
        public async Task Withdrawing_to_scrap_without_the_operations_password_is_rejected()
        {
            await _db.SignInTestUserAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "1234");

            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                    s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage3Id, CompletionDay, 20, null, null, "غلط")));

            Assert.DoesNotContain("مقفول", ex.Message);
        }

        [Fact]
        public async Task Withdrawing_to_scrap_on_a_closed_day_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(CompletionDay);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                    s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage3Id, CompletionDay, 20, null, null, "")));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task Scrapping_from_a_stage_that_isnt_the_ranges_current_position_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day); // range is BagStage3Id -> BagStage3Id

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                    s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage2Id, CompletionDay, 20, null, null, "")));

            Assert.Contains("المرحلة اللي الرصيد واقف فيها", ex.Message);
        }

        [Fact]
        public async Task Scrapping_part_of_a_range_then_withdrawing_the_rest_does_not_allow_overdrawing()
        {
            // نطاق بمرحلتين (BagStage2Id -> BagStage3Id) — سحب هالك من
            // أول مرحلته (20 من 50)، وبعدين سحب إكمال إنتاج عادي. لازم
            // الـ20 المسحوبة هالك تتحسب في "المتاح" حتى إنها اتسجلت على
            // BagStage2Id (مش مرحلة خروج النطاق BagStage3Id)
            await _db.SignInTestUserAsync();

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Mixed withdrawal",
                    Quantity = 50,
                    OriginalDate = Day
                }));

            var range = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 50
                }));

            // سحب 20 هالك من BagStage2Id (مرحلة بداية النطاق)
            await _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage2Id, CompletionDay, 20, null, null, ""));

            // محاولة سحب الـ50 كاملة (بدل الـ30 الباقية فقط) لازم تترفض
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 }
            };
            var overEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                    s.WithdrawAsync(balance.Id,
                        new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 50 } },
                        shares, CompletionDay.AddDays(1), confirmOverride: true)));
            Assert.Contains("أكبر من المتاح", overEx.Message);

            // سحب الـ30 الباقية فعلًا ينجح
            var okShares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 }
            };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 30 } },
                    okShares, CompletionDay.AddDays(1), confirmOverride: true));

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
        }

        [Fact]
        public async Task Deleting_a_scrap_row_linked_to_an_initial_balance_usage_frees_the_usage_and_restores_remaining_quantity()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var scrap = await _db.InScopeAsync<InitialBalanceService, Core.Models.ProductionScrap>(s =>
                s.WithdrawToScrapAsync(balance.Id, range.Id, TestDatabase.BagStage3Id, CompletionDay, 20, null, null, ""));

            using var scope = _db.CreateScope();
            // من غير الإصلاح: ده كان بيرمي DbUpdateException/SQLite Error 19
            // (FOREIGN KEY constraint failed) لأن InitialBalanceUsage
            // لسه ماسكة ProductionScrapId — نفس عيلة باج Task 2
            await _db.GetService<ScrapService>(scope).RemoveAsync(scrap.Id);

            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            Assert.False(await appDb.InitialBalanceUsages.AnyAsync(u => u.ProductionScrapId == scrap.Id));
            Assert.False(await appDb.ProductionScraps.AnyAsync(s => s.Id == scrap.Id));

            var afterDelete = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(afterDelete);
            Assert.Equal(InitialBalanceStatus.Available, afterDelete.Status);
            Assert.Equal(20, afterDelete.RemainingQuantity);
        }

        // ======================= التحقق من تداخل النطاقات =======================

        [Fact]
        public async Task Creating_a_balance_with_two_overlapping_ranges_is_rejected()
        {
            var createReq = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "Overlap on create",
                Quantity = 100,
                OriginalDate = Day,
                Ranges = new System.Collections.Generic.List<AddInitialBalanceRangeRequest>
                {
                    new() { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 30 },
                    // النطاق ده بيتداخل مع اللي فوقه على BagStage2Id
                    new() { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 30 }
                }
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s => s.CreateAsync(createReq)));

            Assert.Contains("متسجلة خلاص", ex.Message);
        }

        [Fact]
        public async Task Adding_a_range_that_overlaps_an_existing_saved_range_is_rejected()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Overlap on add",
                    Quantity = 100,
                    OriginalDate = Day,
                }));

            await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage1Id,
                    ToStageId = TestDatabase.BagStage2Id,
                    PieceCount = 30
                }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                    s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                    {
                        FromStageId = TestDatabase.BagStage2Id,
                        ToStageId = TestDatabase.BagStage3Id,
                        PieceCount = 30
                    })));

            Assert.Contains("متسجلة خلاص", ex.Message);
        }

        // ======================= حذف سجل إكمال رصيد =======================

        private async Task<InitialBalanceDto> CreateBalanceAndCompleteAsync(int pieces, DateTime usedDate)
        {
            await _db.SignInTestUserAsync();

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Balance to delete",
                    Quantity = pieces,
                    OriginalDate = Day,
                }));

            var rangeDto = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = pieces
                }));

            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = pieces },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = pieces }
            };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(
                    balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = rangeDto.Id, PieceCount = pieces } },
                    shares, usedDate, confirmOverride: true));

            var completed = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(completed);
            Assert.Equal(InitialBalanceStatus.Completed, completed.Status);
            return completed;
        }

        [Fact]
        public async Task Deleting_a_balance_completion_record_frees_the_initial_balance_usage_and_restores_remaining_quantity()
        {
            var balance = await CreateBalanceAndCompleteAsync(50, CompletionDay);

            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            // السطر المرتبط بـ InitialBalanceUsage هو سطر مرحلة خروج
            // النطاق (BagStage3Id) بس — شوف تعليق WriteUsageRowsAsync
            var wage = await appDb.DailyProductions.FirstAsync(dp =>
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage3Id && dp.Date == CompletionDay);

            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(wage.Id, "", "تصحيح");

            Assert.True(result.IsDeleted);

            var afterDelete = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(afterDelete);
            Assert.Equal(InitialBalanceStatus.Available, afterDelete.Status);
            Assert.Equal(50, afterDelete.RemainingQuantity);

            Assert.False(await appDb.InitialBalanceUsages.AnyAsync(u => u.DailyProductionId == wage.Id));
        }

        [Fact]
        public async Task Deleting_a_normal_production_record_with_no_balance_usage_still_works_as_before()
        {
            await RecordBaseAsync(TestDatabase.BagStage1Id, 10, Day);

            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            var record = await appDb.DailyProductions.FirstAsync(dp => dp.WorkerId == TestDatabase.WorkerAhmedId);

            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionAsync(record.Id, "", "تصحيح");

            Assert.True(result.IsDeleted);
        }

        [Fact]
        public async Task Deleting_a_whole_day_that_includes_a_balance_completion_record_does_not_crash()
        {
            await CreateBalanceAndCompleteAsync(50, CompletionDay);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(CompletionDay, "", "تصحيح");

            Assert.True(result.IsDeleted);

            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            Assert.Equal(0, await appDb.InitialBalanceUsages.CountAsync());
        }
    }
}