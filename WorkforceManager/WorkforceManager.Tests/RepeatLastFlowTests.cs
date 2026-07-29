using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات "كرّر إدخال يوم فات": الدالة بتدوّر على آخر يوم اشتغل فيه
    /// المنتج وبترجّع توزيع عماله. لازم ترجّع اليوم الصح بالظبط، وتتجاهل
    /// المنتجات التانية، وما ترجّعش أعداد قطع خالص.
    /// </summary>
    public class RepeatLastFlowTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        /// <summary>يكتب سجل إنتاج مباشرة بتاريخ محدد (تجهيز تاريخ للاختبار)</summary>
        private async Task SeedProductionAsync(DateTime date, int stageId, int workerId, int pieces = 10)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            db.DailyProductions.Add(new DailyProduction
            {
                WorkerId = workerId,
                ProductionStageId = stageId,
                Date = date.Date,
                PieceCount = pieces,
                PiecesPerWorkdayAtEntry = 10
            });
            await db.SaveChangesAsync();
        }

        private Task<LastFlowDto?> GetLastFlowAsync(int productId, DateTime before) =>
            _db.InScopeAsync<ProductionFlowService, LastFlowDto?>(service =>
                service.GetLastFlowAsync(productId, before));

        // ---------------- مفيش تاريخ ----------------

        [Fact]
        public async Task NoPreviousProduction_ReturnsNull()
        {
            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);
            Assert.Null(result);
        }

        [Fact]
        public async Task ProductionOnlyOnTheSameDay_IsNotReturned()
        {
            // اليوم نفسه مش "يوم فات" — الدالة بتدوّر على اللي قبله
            await SeedProductionAsync(TestDatabase.Today, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);
            Assert.Null(result);
        }

        // ---------------- بيجيب آخر يوم ----------------

        [Fact]
        public async Task ReturnsTheAssignmentsOfThePreviousDay()
        {
            var yesterday = TestDatabase.Today.AddDays(-1);
            await SeedProductionAsync(yesterday, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);
            await SeedProductionAsync(yesterday, TestDatabase.RingStage2Id, TestDatabase.WorkerSaidId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);

            Assert.NotNull(result);
            Assert.Equal(yesterday.Date, result!.Date);
            Assert.Equal(2, result.Assignments.Count);
            Assert.Contains(result.Assignments, a =>
                a.ProductionStageId == TestDatabase.RingStage1Id && a.WorkerId == TestDatabase.WorkerAhmedId);
            Assert.Contains(result.Assignments, a =>
                a.ProductionStageId == TestDatabase.RingStage2Id && a.WorkerId == TestDatabase.WorkerSaidId);
        }

        [Fact]
        public async Task WithSeveralPastDays_ReturnsOnlyTheMostRecentOne()
        {
            // يوم قديم بأحمد، ويوم أحدث بسعيد — المفروض يرجّع الأحدث بس
            await SeedProductionAsync(TestDatabase.Today.AddDays(-5), TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);
            await SeedProductionAsync(TestDatabase.Today.AddDays(-2), TestDatabase.RingStage1Id, TestDatabase.WorkerSaidId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);

            Assert.NotNull(result);
            Assert.Equal(TestDatabase.Today.AddDays(-2).Date, result!.Date);
            var assignment = Assert.Single(result.Assignments);
            Assert.Equal(TestDatabase.WorkerSaidId, assignment.WorkerId);
        }

        // ---------------- عزل المنتجات ----------------

        [Fact]
        public async Task OtherProductsProduction_IsIgnored()
        {
            // شغل على "سلسلة" بس — السؤال عن "دبلة"
            await SeedProductionAsync(TestDatabase.Today.AddDays(-1), TestDatabase.ChainStage1Id, TestDatabase.WorkerAhmedId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);
            Assert.Null(result);
        }

        [Fact]
        public async Task OnlyTheAskedProductsStagesComeBack()
        {
            var yesterday = TestDatabase.Today.AddDays(-1);
            await SeedProductionAsync(yesterday, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);
            await SeedProductionAsync(yesterday, TestDatabase.ChainStage1Id, TestDatabase.WorkerSaidId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);

            var assignment = Assert.Single(result!.Assignments);
            Assert.Equal(TestDatabase.RingStage1Id, assignment.ProductionStageId);
        }

        // ---------------- تفاصيل ----------------

        [Fact]
        public async Task SameWorkerLoggedTwiceOnAStage_AppearsOnlyOnce()
        {
            var yesterday = TestDatabase.Today.AddDays(-1);
            await SeedProductionAsync(yesterday, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, 10);
            await SeedProductionAsync(yesterday, TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, 15);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);

            Assert.Single(result!.Assignments);
        }

        [Fact]
        public async Task ProductionOlderThanTheLookbackWindow_IsIgnored()
        {
            // 90 يوم بره نافذة البحث الافتراضية (60 يوم)
            await SeedProductionAsync(TestDatabase.Today.AddDays(-90), TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);
            Assert.Null(result);
        }

        [Fact]
        public async Task TheWorkerNameComesBackForDisplay()
        {
            await SeedProductionAsync(TestDatabase.Today.AddDays(-1), TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

            var result = await GetLastFlowAsync(TestDatabase.ProductRingId, TestDatabase.Today);

            Assert.Equal("أحمد", Assert.Single(result!.Assignments).WorkerName);
        }
    }
}
