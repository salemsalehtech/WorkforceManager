using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// معامل صعوبة المرحلة (ProductionStage.DifficultyMultiplier) بيُستخدم
    /// بس في ترتيب "أحسن عامل" — أهم ضمانة هنا إنه **مايتسرّبش** لأي حساب
    /// أجر أو تقرير إنتاج. الاختبارات دي بتسجّل إنتاج، تحسب تقرير العامل،
    /// تغيّر المعامل لقيمة متطرفة، وتتأكد إن نفس الأرقام رجعت بالضبط.
    /// </summary>
    public class StageDifficultyMultiplierTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(int stageId, int pieces, int workerId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, Day, confirmOverride: true);
        }

        private async Task SetDifficultyAsync(int stageId, decimal multiplier)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var stage = await db.ProductionStages.FirstAsync(s => s.Id == stageId);
            stage.DifficultyMultiplier = multiplier;
            await db.SaveChangesAsync();
        }

        private async Task<Business.DTOs.WorkerProductionReportDto> ReportAsync(int workerId) =>
            await _db.InScopeAsync<ProductionReportService, Business.DTOs.WorkerProductionReportDto>(
                service => service.GetWorkerReportAsync(workerId, Day, Day));

        [Fact]
        public async Task NewStages_DefaultToNormalDifficulty()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var stage = await db.ProductionStages.FirstAsync(s => s.Id == TestDatabase.RingStage1Id);

            Assert.Equal(1.0m, stage.DifficultyMultiplier);
        }

        [Fact]
        public async Task ChangingAStagesDifficulty_NeverChangesTheWorkersReportOrWage()
        {
            await RecordAsync(TestDatabase.RingStage1Id, 5000, TestDatabase.WorkerAhmedId);

            var before = await ReportAsync(TestDatabase.WorkerAhmedId);

            await SetDifficultyAsync(TestDatabase.RingStage1Id, 3.0m);

            var after = await ReportAsync(TestDatabase.WorkerAhmedId);

            Assert.Equal(before.TotalPieces, after.TotalPieces);
            Assert.Equal(before.ProducedWorkdays, after.ProducedWorkdays);
            Assert.Equal(before.NetWorkdays, after.NetWorkdays);
            Assert.Equal(before.NetWageEgp, after.NetWageEgp);
        }

        [Fact]
        public async Task AddStageAsync_RejectsAZeroOrNegativeDifficulty()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<ProductManagementService, ProductionStage>(service =>
                    service.AddStageAsync(TestDatabase.ProductRingId, "مرحلة جديدة", 100, difficultyMultiplier: 0m)));
        }
    }
}
