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
                Reason = "Leftovers"
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
        public async Task RecordUsage_CreatesWageButExcludesFromOutputOnCompletionDate()
        {
            // Arrange: sign in test user for operations password bypass (if any)
            await _db.SignInTestUserAsync();

            var createReq = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "Balance B",
                Quantity = 50,
                OriginalDate = Day,
                Reason = "Unfinished goods"
            };

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s => s.CreateAsync(createReq));
            
            var rangeDto = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 50
                }));

            var balanceNullable = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(balanceNullable);
            balance = balanceNullable;

            // baseline stage 3 totals before completion
            var baseS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, CompletionDay);
            Assert.Equal(0, baseS3);

            // Act: complete the 50 pieces at Stage 3 on CompletionDay
            // Using worker Ahmed who is qualified
            await _db.InScopeAsync<InitialBalanceService, InitialBalanceUsageDto>(s =>
                s.RecordUsageAsync(new RecordInitialBalanceUsageRequest
                {
                    InitialBalanceId = balance.Id,
                    InitialBalanceRangeId = balance.Ranges.First().Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.BagStage3Id,
                    Quantity = 50,
                    UsedDate = CompletionDay,
                    OperationsPassword = "" // Password check may pass if gate is unconfigured or user is test
                }));

            // Assert: Balance updated
            var updatedNullable = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(balance.Id));
            Assert.NotNull(updatedNullable);
            var updated = updatedNullable;
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
            Assert.Equal(50, updated.UsedQuantity);

            // Assert: Wage created on CompletionDay
            // The daily production for Ahmed should exist with IsBalanceCompletion = true
            using var scope = _db.CreateScope();
            var appDb = _db.GetService<WorkforceManager.Data.AppDbContext>(scope);
            var wage = await appDb.DailyProductions.FirstOrDefaultAsync(dp => 
                dp.WorkerId == TestDatabase.WorkerAhmedId && dp.Date == CompletionDay);
            
            Assert.NotNull(wage);
            Assert.True(wage.IsBalanceCompletion);
            Assert.Equal(50, wage.PieceCount);

            // Assert: Stage 3 output on original date (Day) increased by 50
            var origDateS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, Day);
            Assert.Equal(50, origDateS3);

            // Assert: No double counting - stage 3 output on CompletionDay is still 50, not 100
            var compDateS3 = await GetStageTotalAsync(TestDatabase.BagStage3Id, CompletionDay);
            Assert.Equal(50, compDateS3);
        }
    }
}