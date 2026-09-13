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
        public async Task Withdrawing_from_a_balance_counts_as_completely_normal_production_in_reports()
        {
            // بعكس التصميم القديم (كان بيستبعد قطع الإكمال من التقارير عشان
            // كان بيسجّل الإنتاج الفعلي على تاريخ الرصيد الأصلي فمكانش
            // ينفع يتحسب قطع تانية يوم الإكمال). دلوقتي السحب رحلة إنتاج
            // عادية بالكامل (WithdrawAsync عبر RecordFlowAsync)، فقطعها
            // وأجرها بيتحسبوا زي أي رحلة عادية — مفيش استبعاد.
            await _db.SignInTestUserAsync();

            var createRequest = new CreateInitialBalanceRequest
            {
                ProductId = TestDatabase.ProductBagId,
                Name = "رصيد تقريري",
                Quantity = 30,
                OriginalDate = Day,
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

            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 }
            };
            await _db.InScopeAsync<InitialBalanceService, FlowSaveResultDto>(s =>
                s.WithdrawAsync(
                    balance.Id,
                    new[] { new InitialBalanceRangeWithdrawalDto { RangeId = rangeDto.Id, PieceCount = 30 } },
                    shares, CompletionDay, confirmOverride: true));

            var workerReport = await _db.InScopeAsync<ProductionReportService, WorkerProductionReportDto>(s =>
                s.GetWorkerReportAsync(TestDatabase.WorkerAhmedId, Day, CompletionDay));

            // العامل لمس 30 قطعة على كل من BagStage2Id وBagStage3Id = 60
            Assert.Equal(60, workerReport.TotalPieces);
            Assert.True(workerReport.ProducedWorkdays > 0);

            var weekly = await _db.InScopeAsync<WeeklySummaryService, WorkerWeeklySummaryDto?>(s =>
                s.GetWorkerWeeklySummaryAsync(TestDatabase.WorkerAhmedId, CompletionDay));

            Assert.NotNull(weekly);
            Assert.Equal(60, weekly.TotalPieces);
            Assert.True(weekly.ProducedWorkdays > 0);
        }
    }
}
