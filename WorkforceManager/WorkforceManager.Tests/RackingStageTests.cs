using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// مرحلة الرص الإدارية: مرحلة حقيقية في خط الإنتاج (ليها ترتيب، تظهر
    /// في شاشة المنتج) — بس **برّه الخط المحسوب تمامًا**
    /// (ProductionLine.Active) فمالهاش نطاق قطع خالص. عاملها بيتحط تاج
    /// عليه (FlowTaggedWorkerDto يشاور على مرحلة الرص نفسها) بيومية
    /// كاملة تلقائيًا، من غير قطع ومن غير تحقق تأهيل — نفس آلية تاج
    /// المتدرّب بالحرف (شوف TaggedWorkerTests). Product.RackingWorkerId
    /// دلوقتي مجرد افتراضي تتحط الواجهة تاجه تلقائيًا كل يوم — الخدمة
    /// نفسها ما بتقراهوش ولا بتسجل حضور بيه تلقائيًا.
    ///
    /// خط الشنطة: 1.قص → 2.خياطة → 3.تشطيب (اليومية 10 لكل مرحلة).
    /// عامل الرص المزروع (WorkerMonaHourlyId) دوره Racking فعلاً.
    /// </summary>
    public class RackingStageTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;

        // ======================= إضافة المرحلة =======================

        [Fact]
        public async Task AddingARackingStage_MarksItAndPutsItLast()
        {
            using var scope = _db.CreateScope();
            var stage = await _db.GetService<ProductManagementService>(scope)
                .AddRackingStageAsync(TestDatabase.ProductBagId);

            Assert.True(stage.IsRackingStage);
            Assert.Equal("رص", stage.StageName);

            var db = _db.GetService<AppDbContext>(scope);
            var product = await db.Products.FindAsync(TestDatabase.ProductBagId);
            var maxOrder = db.ProductionStages.Where(s => s.ProductId == product!.Id).Max(s => s.SortOrder);
            Assert.Equal(maxOrder, stage.SortOrder); // آخر ترتيب
        }

        [Fact]
        public async Task AProductCanOnlyHaveOneRackingStage()
        {
            using var scope = _db.CreateScope();
            var mgmt = _db.GetService<ProductManagementService>(scope);
            await mgmt.AddRackingStageAsync(TestDatabase.ProductBagId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                mgmt.AddRackingStageAsync(TestDatabase.ProductBagId));

            Assert.Contains("عنده مرحلة رص خلاص", ex.Message);
        }

        [Fact]
        public async Task ARackingStage_IsExcludedFromTheActiveProductionLine()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            await _db.GetService<ProductManagementService>(scope)
                .AddRackingStageAsync(TestDatabase.ProductBagId);

            var product = await db.Products
                .Include(p => p.Stages)
                .FirstAsync(p => p.Id == TestDatabase.ProductBagId);

            var line = ProductionLine.Active(product);

            Assert.DoesNotContain(line, s => s.IsRackingStage);
            Assert.Equal(3, line.Count); // قص/خياطة/تشطيب بس — الرص مش منهم
            Assert.Equal(TestDatabase.BagStage3Id, ProductionLine.LastForHistory(product)!.Id);
        }

        // ======================= عامل الرص الثابت =======================

        [Fact]
        public async Task SettingAndClearingTheRackingWorker_Works()
        {
            using var scope = _db.CreateScope();
            var mgmt = _db.GetService<ProductManagementService>(scope);

            await mgmt.SetRackingWorkerAsync(TestDatabase.ProductBagId, TestDatabase.WorkerMonaHourlyId);

            var db = _db.GetService<AppDbContext>(scope);
            var product = await db.Products.FindAsync(TestDatabase.ProductBagId);
            Assert.Equal(TestDatabase.WorkerMonaHourlyId, product!.RackingWorkerId);

            await mgmt.SetRackingWorkerAsync(TestDatabase.ProductBagId, null);
            db.ChangeTracker.Clear();
            product = await db.Products.FindAsync(TestDatabase.ProductBagId);
            Assert.Null(product!.RackingWorkerId);
        }

        // ======================= أثناء تسجيل الإنتاج =======================

        private async Task RecordAsync(
            int stageId, int pieces, int workerId = TestDatabase.WorkerAhmedId,
            IReadOnlyList<FlowTaggedWorkerDto>? taggedWorkers = null)
        {
            var range = new FlowRangeDto { FromStageId = stageId, ToStageId = stageId, PieceCount = pieces };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = stageId, WorkerId = workerId, PieceCount = pieces }
            };

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, Day1, new[] { range }, shares,
                taggedWorkers: taggedWorkers, confirmOverride: true);
        }

        [Fact]
        public async Task RecordingProduction_WithNoRackingWorkerConfigured_SavesWithoutAnyProblem()
        {
            // مفيش عامل رص متحدَّد للمنتج ده — لازم يحفظ عادي تمامًا
            await RecordAsync(TestDatabase.BagStage1Id, 100);

            using var scope = _db.CreateScope();
            var report = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day1);

            Assert.Equal(100, report.TotalStartedPieces);
        }

        [Fact]
        public async Task TaggingTheRackingWorkerOnTheRackingStage_RecordsPresenceAndAFullWorkday()
        {
            int rackingStageId;
            using (var scope = _db.CreateScope())
                rackingStageId = (await _db.GetService<ProductManagementService>(scope)
                    .AddRackingStageAsync(TestDatabase.ProductBagId)).Id;

            await RecordAsync(TestDatabase.BagStage1Id, 100, taggedWorkers: new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = rackingStageId, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            var attendance = await db.Attendances.FirstOrDefaultAsync(a =>
                a.WorkerId == TestDatabase.WorkerMonaHourlyId && a.Date == Day1.Date);
            Assert.NotNull(attendance);
            Assert.Equal(Core.Enums.AttendanceStatus.Present, attendance!.Status);

            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h =>
                h.WorkerId == TestDatabase.WorkerMonaHourlyId && h.Date == Day1.Date);
            Assert.NotNull(log);
            Assert.Equal(1.0m, log!.WorkdaysCredited);
        }

        [Fact]
        public async Task TheRackingWorkersOwnHourlyLog_DoesNotRequireAnyPieceCountFromThem()
        {
            int rackingStageId;
            using (var scope = _db.CreateScope())
                rackingStageId = (await _db.GetService<ProductManagementService>(scope)
                    .AddRackingStageAsync(TestDatabase.ProductBagId)).Id;

            // عامل الرص متحط عليه تاج بس، مش موجود في shares خالص — ده أساس الفصل
            await RecordAsync(TestDatabase.BagStage1Id, 50, TestDatabase.WorkerAhmedId, new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = rackingStageId, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            var records = await _db.GetProductionAsync();
            Assert.DoesNotContain(records, r => r.WorkerId == TestDatabase.WorkerMonaHourlyId);
        }

        [Fact]
        public async Task SettingTheDefaultRackingWorker_DoesNotByItselfRecordAnyAttendance()
        {
            // Product.RackingWorkerId دلوقتي افتراضي للواجهة بس — الخدمة
            // ما بتقراهوش ولا بتسجل حضور بيه لوحدها من غير تاج فعلي
            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .SetRackingWorkerAsync(TestDatabase.ProductBagId, TestDatabase.WorkerMonaHourlyId);

            await RecordAsync(TestDatabase.BagStage1Id, 100);

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);
            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h =>
                h.WorkerId == TestDatabase.WorkerMonaHourlyId && h.Date == Day1.Date);
            Assert.Null(log);
        }
    }
}
