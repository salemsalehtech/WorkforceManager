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

        // ======================= تعديل الاسم/الملاحظات (UpdateAsync) =======================

        [Fact]
        public async Task UpdateAsync_changes_name_and_notes_but_not_quantity()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Old name",
                    Quantity = 30,
                    OriginalDate = Day
                }));

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.UpdateAsync(balance.Id, "New name", "ملاحظة جديدة"));

            Assert.Equal("New name", updated.Name);
            Assert.Equal("ملاحظة جديدة", updated.Notes);
            Assert.Equal(30, updated.Quantity);
        }

        [Fact]
        public async Task UpdateAsync_with_blank_name_is_rejected()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Old name",
                    Quantity = 30,
                    OriginalDate = Day
                }));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                    s.UpdateAsync(balance.Id, "   ", null)));
        }

        // ======================= سجل الرصيد (GetHistoryAsync) بيعرض كل العمال/المراحل =======================

        [Fact]
        public async Task GetHistoryAsync_shows_every_worker_and_stage_in_a_multi_stage_range_not_just_the_exit_stage()
        {
            await _db.SignInTestUserAsync();

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Multi-worker range",
                    Quantity = 50,
                    OriginalDate = Day,
                }));

            var range = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 50
                }));

            // عاملين مختلفين على المرحلة الوسيطة (BagStage2Id) ومرحلة الخروج (BagStage3Id)
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 50 }
            };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 50 } },
                    shares, CompletionDay, confirmOverride: true));

            var history = await _db.InScopeAsync<InitialBalanceService, System.Collections.Generic.IReadOnlyList<InitialBalanceUsageDto>>(s =>
                s.GetHistoryAsync(balance.Id));

            // قبل الإصلاح: صف واحد بس (مرحلة الخروج). دلوقتي: صف لكل عامل/مرحلة
            Assert.Equal(2, history.Count);
            Assert.Contains(history, h => h.WorkerId == TestDatabase.WorkerAhmedId && h.ProductionStageId == TestDatabase.BagStage2Id);
            Assert.Contains(history, h => h.WorkerId == TestDatabase.WorkerSaidId && h.ProductionStageId == TestDatabase.BagStage3Id);

            // لكن "المتاح/المستهلك" من الرصيد يفضل محسوب زي الأول بالظبط —
            // بيتحسب مرة واحدة بس (مش مرتين لمرحلتين)
            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Equal(50, updated.UsedQuantity);
            Assert.Equal(0, updated.RemainingQuantity);
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
        }

        // ======================= التعديل الشامل (EditAsync) =======================

        [Fact]
        public async Task EditAsync_can_add_a_new_range_resize_an_untouched_one_and_drop_another()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Edit target",
                    Quantity = 100,
                    OriginalDate = Day,
                    Ranges = new System.Collections.Generic.List<AddInitialBalanceRangeRequest>
                    {
                        new() { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 20 },
                        new() { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 30 }
                    }
                }));

            var range1 = balance.Ranges.Single(r => r.FromStageId == TestDatabase.BagStage1Id);
            // range2 (BagStage2Id) بيتشال، range1 بيتصغّر لـ10، ونطاق جديد على BagStage3Id بيتضاف
            var edited = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.EditAsync(balance.Id, "Edited name", "ملاحظة", new System.Collections.Generic.List<InitialBalanceRangeEditItem>
                {
                    new() { Id = range1.Id, FromStageId = range1.FromStageId, ToStageId = range1.ToStageId, PieceCount = 10 },
                    new() { Id = null, FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 25 }
                }));

            Assert.Equal("Edited name", edited.Name);
            Assert.Equal(2, edited.Ranges.Count);
            Assert.Contains(edited.Ranges, r => r.FromStageId == TestDatabase.BagStage1Id && r.PieceCount == 10);
            Assert.Contains(edited.Ranges, r => r.FromStageId == TestDatabase.BagStage3Id && r.PieceCount == 25);
            Assert.DoesNotContain(edited.Ranges, r => r.FromStageId == TestDatabase.BagStage2Id);
        }

        [Fact]
        public async Task EditAsync_rejects_dropping_a_range_that_has_usage()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 10 } },
                    shares, CompletionDay, confirmOverride: true));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                    s.EditAsync(balance.Id, balance.Name, null, new System.Collections.Generic.List<InitialBalanceRangeEditItem>())));

            Assert.Contains("عليه استخدام", ex.Message);
        }

        [Fact]
        public async Task EditAsync_rejects_shrinking_a_used_range_below_its_used_quantity()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                    s.EditAsync(balance.Id, balance.Name, null, new System.Collections.Generic.List<InitialBalanceRangeEditItem>
                    {
                        new() { Id = range.Id, FromStageId = range.FromStageId, ToStageId = range.ToStageId, PieceCount = 19 }
                    })));

            Assert.Contains("أقل من الكمية المستخدمة", ex.Message);
        }

        // ======================= تعديل النطاقات (UpdateRangeAsync/RemoveRangeAsync) =======================

        [Fact]
        public async Task Shrinking_a_range_with_no_usage_to_any_positive_count_succeeds()
        {
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.UpdateRangeAsync(range.Id, 10));

            Assert.Equal(10, updated.PieceCount);
        }

        [Fact]
        public async Task Shrinking_a_range_below_its_used_quantity_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                    s.UpdateRangeAsync(range.Id, 19)));

            Assert.Contains("أقل من الكمية المستخدمة", ex.Message);

            // بالظبط على الأرضية (19+1؟ لأ، المستخدم=20) - تحديث لنفس المستخدم ينجح
            var atFloor = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.UpdateRangeAsync(range.Id, 20));
            Assert.Equal(20, atFloor.PieceCount);
        }

        [Fact]
        public async Task Growing_a_range_beyond_the_balances_total_quantity_is_rejected()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Two ranges",
                    Quantity = 100,
                    OriginalDate = Day,
                }));

            var rangeA = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage1Id,
                    ToStageId = TestDatabase.BagStage1Id,
                    PieceCount = 30
                }));
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage2Id,
                    PieceCount = 50
                }));

            // rangeB وحده 50 — تكبير rangeA لـ 51 يخلي المجموع 101 > 100
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                    s.UpdateRangeAsync(rangeA.Id, 51)));

            Assert.Contains("أكبر من كمية الرصيد الكلية", ex.Message);
        }

        [Fact]
        public async Task Removing_a_range_that_has_any_usage_is_rejected()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 10 } },
                    shares, CompletionDay, confirmOverride: true));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var scope = _db.CreateScope();
                await _db.GetService<InitialBalanceService>(scope).RemoveRangeAsync(range.Id);
            });

            Assert.Contains("عليه استخدام", ex.Message);
        }

        [Fact]
        public async Task Removing_a_range_with_no_usage_still_works_as_before()
        {
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).RemoveRangeAsync(range.Id);

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updated);
            Assert.Empty(updated.Ranges);
        }

        // ======================= الحذف: Hard delete لو صفر استخدام، Soft-close لو فيه استخدام =======================

        [Fact]
        public async Task Deleting_a_balance_with_zero_usage_removes_the_row_permanently()
        {
            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "No usage yet",
                    Quantity = 30,
                    OriginalDate = Day
                }));

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).DeleteAsync(balance.Id, null, "اتضاف بالغلط");

            using var checkScope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(checkScope);
            Assert.False(await appDb.InitialBalances.IgnoreQueryFilters().AnyAsync(b => b.Id == balance.Id));
        }

        [Fact]
        public async Task Deleting_a_balance_with_partial_usage_soft_closes_it_and_keeps_history_untouched()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 20 } },
                    shares, CompletionDay, confirmOverride: true));

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).DeleteAsync(balance.Id, null, "اتقفل يدويًا");

            using var checkScope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(checkScope);

            // الصف لسه موجود (Soft delete) بس محذوف
            var raw = await appDb.InitialBalances.IgnoreQueryFilters().SingleAsync(b => b.Id == balance.Id);
            Assert.True(raw.IsDeleted);

            // الاستخدام والأجر الحقيقي المرتبط بيه متلمسوش
            Assert.True(await appDb.InitialBalanceUsages.AnyAsync(u => u.InitialBalanceId == balance.Id));
            Assert.True(await appDb.DailyProductions.AnyAsync(dp =>
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.ProductionStageId == TestDatabase.BagStage3Id && dp.Date == CompletionDay));

            // اختفى من القوايم النشطة والملخّص
            var active = await _db.InScopeAsync<InitialBalanceService, System.Collections.Generic.IReadOnlyList<InitialBalanceDto>>(s =>
                s.GetForProductAsync(TestDatabase.ProductBagId));
            Assert.DoesNotContain(active, b => b.Id == balance.Id);
        }

        [Fact]
        public async Task Soft_closed_balance_can_never_be_withdrawn_from_again()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(50, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 10 } },
                    shares, CompletionDay, confirmOverride: true));

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).DeleteAsync(balance.Id, null, "اتقفل يدويًا");

            var moreShares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 5 } };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                    s.WithdrawAsync(balance.Id,
                        new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 5 } },
                        moreShares, CompletionDay.AddDays(1), confirmOverride: true)));

            Assert.Contains("الرصيد الأولي غير موجود", ex.Message);
        }

        // ======================= History (مشتقة من Status == Completed) =======================

        [Fact]
        public async Task A_balance_fully_withdrawn_moves_out_of_active_and_into_history()
        {
            var balance = await CreateBalanceAndCompleteAsync(20, CompletionDay);

            var active = await _db.InScopeAsync<InitialBalanceService, System.Collections.Generic.IReadOnlyList<InitialBalanceDto>>(s =>
                s.GetForProductAsync(TestDatabase.ProductBagId));
            Assert.DoesNotContain(active, b => b.Id == balance.Id);

            var history = await _db.InScopeAsync<InitialBalanceService, System.Collections.Generic.IReadOnlyList<InitialBalanceDto>>(s =>
                s.GetHistoryForProductAsync(TestDatabase.ProductBagId));
            Assert.Contains(history, b => b.Id == balance.Id);
        }

        [Fact]
        public async Task A_manually_deleted_balance_never_appears_in_history_even_with_prior_usage()
        {
            await _db.SignInTestUserAsync();
            var (balance, range) = await CreateSingleStageBalanceAsync(20, Day);

            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10 } };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 10 } },
                    shares, CompletionDay, confirmOverride: true));

            using (var scope = _db.CreateScope())
                await _db.GetService<InitialBalanceService>(scope).DeleteAsync(balance.Id, null, "اتقفل يدويًا");

            var history = await _db.InScopeAsync<InitialBalanceService, System.Collections.Generic.IReadOnlyList<InitialBalanceDto>>(s =>
                s.GetHistoryForProductAsync(TestDatabase.ProductBagId));
            Assert.DoesNotContain(history, b => b.Id == balance.Id);
        }

        [Fact]
        public async Task Product_summary_excludes_completed_balances_so_it_matches_the_visible_active_cards()
        {
            // رصيد كمّل بالكامل (20) + رصيد نشط لسه (30) لنفس المنتج
            await CreateBalanceAndCompleteAsync(20, CompletionDay);
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(new CreateInitialBalanceRequest
                {
                    ProductId = TestDatabase.ProductBagId,
                    Name = "Still active",
                    Quantity = 30,
                    OriginalDate = Day
                }));

            var summary = await _db.InScopeAsync<InitialBalanceService, InitialBalanceSummaryDto>(s =>
                s.GetProductSummaryAsync(TestDatabase.ProductBagId));

            // الملخّص لازم يعكس النشط بس (30)، مش يجمع الرصيد المكتمل (20) فوقه
            Assert.Equal(30, summary.TotalQuantity);
            Assert.Equal(0, summary.UsedQuantity);
            Assert.Equal(30, summary.RemainingQuantity);
            Assert.Equal(1, summary.ActiveBalanceCount);
        }

        // ملحوظة: اختصار "صفر نطاقات = الخط كله" اتنفّذ في الـ UI
        // (DailyEntryViewModel.AddInitialBalanceAsync) مش هنا — CreateAsync
        // نفسها لازم تفضل "بتنشئ بالظبط اللي اتبعتلها" من غير أي تحويل ضمني،
        // لأن أنماط كتير من الاختبارات هنا (وقراءات الكود التلقائي زي
        // ProductionFlowService عن طريق IInitialBalanceRepository) بتعتمد
        // على إن رصيد بصفر نطاقات معناه "كله Unranged لحد ما حد يحدده لاحقًا"،
        // مش "الخط كله تلقائيًا".

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