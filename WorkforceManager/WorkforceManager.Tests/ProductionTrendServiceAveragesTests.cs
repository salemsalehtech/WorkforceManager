using WorkforceManager.Business.Services;
using WorkforceManager.Business.DTOs;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// ProductionTrendService.GetAllWorkerAveragesAsync — لجدول "متوسط
    /// إنتاج العمال" الكامل (بعكس GetDecliningWorkersAsync اللي بترجّع
    /// المتراجعين بس). المحور: كل عامل عنده تاريخ كافي بيظهر، مرتبين
    /// تنازليًا بمتوسطهم.
    /// </summary>
    public class ProductionTrendServiceAveragesTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task RecordDaysAsync(int workerId, int stageId, DateTime today, int daysBack, int piecesPerDay)
        {
            for (var i = 1; i <= daysBack; i++)
            {
                using var scope = _db.CreateScope();
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    workerId, stageId, piecesPerDay, today.AddDays(-i));
            }
        }

        [Fact]
        public async Task GetAllWorkerAveragesAsync_OrdersWorkersByTrailingAverage_HighestFirst()
        {
            var today = TestDatabase.Today;

            // أحمد: متوسط 3000، سعيد: متوسط 5000 — سعيد لازم يظهر الأول
            await RecordDaysAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, today, 7, 3000);
            await RecordDaysAsync(TestDatabase.WorkerSaidId, TestDatabase.RingStage1Id, today, 7, 5000);

            var averages = await _db.InScopeAsync<ProductionTrendService, List<WorkerProductionAverageDto>>(
                service => service.GetAllWorkerAveragesAsync(today));

            Assert.Equal(2, averages.Count);
            Assert.Equal(TestDatabase.WorkerSaidId, averages[0].WorkerId);
            Assert.Equal(5000m, averages[0].TrailingAverage);
            Assert.Equal(TestDatabase.WorkerAhmedId, averages[1].WorkerId);
            Assert.Equal(3000m, averages[1].TrailingAverage);
        }

        [Fact]
        public async Task GetAllWorkerAveragesAsync_ExcludesWorkersWithFewerThanSevenPriorDays()
        {
            var today = TestDatabase.Today;

            // أحمد: 3 أيام بس — مش كفاية
            await RecordDaysAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, today, 3, 3000);

            var averages = await _db.InScopeAsync<ProductionTrendService, List<WorkerProductionAverageDto>>(
                service => service.GetAllWorkerAveragesAsync(today));

            Assert.Empty(averages);
        }
    }
}
