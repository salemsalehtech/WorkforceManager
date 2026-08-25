using System;
using System.Threading.Tasks;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    public class InitialBalanceReportsTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;
        private static DateTime CompletionDay => Day.AddDays(2);

        [Fact]
        public async Task BalanceCompletion_IsExcludedFromPieceTotals_ButRetainsWorkdays()
        {
            await _db.SignInTestUserAsync();

            var createRequest = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "رصيد تقريري",
                Quantity = 30,
                OriginalDate = Day,
                Reason = "اختبار تقارير"
            };

            var balance = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto>(s =>
                s.CreateAsync(createRequest));

            var rangeDto = await _db.InScopeAsync<InitialBalanceService, InitialBalanceRangeDto>(s =>
                s.AddRangeAsync(balance.Id, new AddInitialBalanceRangeRequest
                {
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 30
                }));

            await _db.InScopeAsync<InitialBalanceService, InitialBalanceUsageDto>(s =>
                s.RecordUsageAsync(new RecordInitialBalanceUsageRequest
                {
                    InitialBalanceId = balance.Id,
                    InitialBalanceRangeId = rangeDto.Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.BagStage3Id,
                    Quantity = 30,
                    UsedDate = CompletionDay,
                    OperationsPassword = ""
                }));

            var workerReport = await _db.InScopeAsync<ProductionReportService, WorkerProductionReportDto>(s =>
                s.GetWorkerReportAsync(TestDatabase.WorkerAhmedId, Day, CompletionDay));

            Assert.Equal(0, workerReport.TotalPieces);
            Assert.True(workerReport.ProducedWorkdays > 0);

            var weekly = await _db.InScopeAsync<WeeklySummaryService, WorkerWeeklySummaryDto?>(s =>
                s.GetWorkerWeeklySummaryAsync(TestDatabase.WorkerAhmedId, CompletionDay));

            Assert.NotNull(weekly);
            Assert.Equal(0, weekly.TotalPieces);
            Assert.True(weekly.ProducedWorkdays > 0);
        }
    }
}
