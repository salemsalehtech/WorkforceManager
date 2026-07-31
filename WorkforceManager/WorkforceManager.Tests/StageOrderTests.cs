using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات ترتيب مراحل خط الإنتاج. الترتيب مش شكلي: نطاق التسجيل
    /// ("من مرحلة كذا لمرحلة كذا") بيتحسب بترتيب المراحل، فترتيب غلط
    /// معناه إنتاج بيتسجل على مراحل غير اللي المستخدم قصدها.
    /// </summary>
    public class StageOrderTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        /// <summary>مراحل منتج معين بترتيب الخط الفعلي</summary>
        private async Task<List<ProductionStage>> GetRingStagesInOrderAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            return await db.ProductionStages
                .Where(s => s.ProductId == TestDatabase.ProductRingId)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .AsNoTracking()
                .ToListAsync();
        }

        private Task<bool> MoveAsync(int stageId, bool up) =>
            _db.InScopeAsync<ProductManagementService, bool>(service =>
                service.MoveStageAsync(stageId, up));

        // ---------------- الحركة الأساسية ----------------

        [Fact]
        public async Task MovingFirstStageDown_SwapsItWithTheNextOne()
        {
            var before = await GetRingStagesInOrderAsync();
            Assert.Equal("تشكيل", before[0].StageName);
            Assert.Equal("تلميع", before[1].StageName);

            var moved = await MoveAsync(TestDatabase.RingStage1Id, up: false);
            Assert.True(moved);

            var after = await GetRingStagesInOrderAsync();
            Assert.Equal("تلميع", after[0].StageName);
            Assert.Equal("تشكيل", after[1].StageName);
        }

        [Fact]
        public async Task MovingLastStageUp_SwapsItWithThePreviousOne()
        {
            var moved = await MoveAsync(TestDatabase.RingStage2Id, up: true);
            Assert.True(moved);

            var after = await GetRingStagesInOrderAsync();
            Assert.Equal("تلميع", after[0].StageName);
            Assert.Equal("تشكيل", after[1].StageName);
        }

        // ---------------- حدود الخط ----------------

        [Fact]
        public async Task MovingFirstStageUp_DoesNothing()
        {
            var moved = await MoveAsync(TestDatabase.RingStage1Id, up: true);
            Assert.False(moved);

            // الترتيب زي ما هو بالظبط
            var after = await GetRingStagesInOrderAsync();
            Assert.Equal("تشكيل", after[0].StageName);
            Assert.Equal("تلميع", after[1].StageName);
        }

        [Fact]
        public async Task MovingLastStageDown_DoesNothing()
        {
            var moved = await MoveAsync(TestDatabase.RingStage2Id, up: false);
            Assert.False(moved);

            var after = await GetRingStagesInOrderAsync();
            Assert.Equal("تشكيل", after[0].StageName);
            Assert.Equal("تلميع", after[1].StageName);
        }

        [Fact]
        public async Task MovingTheOnlyStageOfAProduct_DoesNothing()
        {
            // منتج "سلسلة" له مرحلة واحدة بس
            Assert.False(await MoveAsync(TestDatabase.ChainStage1Id, up: true));
            Assert.False(await MoveAsync(TestDatabase.ChainStage1Id, up: false));
        }

        // ---------------- إعادة الترقيم ----------------

        [Fact]
        public async Task MovingAStage_RenumbersTheWholeLineFromOne()
        {
            await MoveAsync(TestDatabase.RingStage1Id, up: false);

            var after = await GetRingStagesInOrderAsync();

            // الترتيب بيبقى 1، 2، 3... من غير فجوات ولا تكرار
            Assert.Equal(Enumerable.Range(1, after.Count), after.Select(s => s.SortOrder));
        }

        [Fact]
        public async Task MovingAStage_DoesNotTouchOtherProductsStages()
        {
            await MoveAsync(TestDatabase.RingStage1Id, up: false);

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var chainStage = await db.ProductionStages
                .AsNoTracking()
                .FirstAsync(s => s.Id == TestDatabase.ChainStage1Id);

            Assert.Equal(1, chainStage.SortOrder);
        }

        [Fact]
        public async Task MovingBackAndForth_ReturnsToTheOriginalOrder()
        {
            await MoveAsync(TestDatabase.RingStage1Id, up: false);
            await MoveAsync(TestDatabase.RingStage1Id, up: true);

            var after = await GetRingStagesInOrderAsync();
            Assert.Equal("تشكيل", after[0].StageName);
            Assert.Equal("تلميع", after[1].StageName);
        }

        // ---------------- الأثر الحقيقي: نطاق التسجيل بيمشي بالترتيب الجديد ----------------

        [Fact]
        public async Task AfterReorder_ProductionRangeFollowsTheNewOrder()
        {
            // قبل الترتيب: "من تشكيل إلى تلميع" نطاق صحيح (تشكيل الأولى)
            await MoveAsync(TestDatabase.RingStage1Id, up: false); // تلميع بقت الأولى

            // بعد الترتيب: "من تشكيل إلى تلميع" بقى نطاق معكوس ولازم يترفض،
            // لأن تشكيل بقت بعد تلميع في الخط.
            // لازم نبعت توزيع عمال مش فاضي، لأن الخدمة بترفض القائمة الفاضية
            // قبل ما توصل لفحص ترتيب النطاق أصلاً
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                    service.RecordFlowAsync(
                        TestDatabase.ProductRingId, TestDatabase.Today,
                        new[]
                        {
                            new BatchRangeDto
                            {
                                FromStageId = TestDatabase.RingStage1Id, // تشكيل (بقت التانية)
                                ToStageId = TestDatabase.RingStage2Id,   // تلميع (بقت الأولى)
                                PieceCount = 10
                            }
                        },
                        new[]
                        {
                            new FlowShareDto
                            {
                                ProductionStageId = TestDatabase.RingStage1Id,
                                WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10
                            }
                        })));

            Assert.Contains("بتيجي بعد", ex.Message);
        }

        [Fact]
        public async Task AfterReorder_TheReversedRangeBecomesValid()
        {
            await MoveAsync(TestDatabase.RingStage1Id, up: false); // تلميع الأولى، تشكيل التانية

            // "من تلميع إلى تشكيل" بقى النطاق الصحيح دلوقتي
            var result = await _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    TestDatabase.ProductRingId, TestDatabase.Today,
                    new[]
                    {
                        new BatchRangeDto
                        {
                            FromStageId = TestDatabase.RingStage2Id, // تلميع
                            ToStageId = TestDatabase.RingStage1Id,   // تشكيل
                            PieceCount = 10
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage2Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 10
                        },
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage1Id,
                            WorkerId = TestDatabase.WorkerSaidId, PieceCount = 10
                        }
                    },
                    confirmOverride: true));

            Assert.Equal(2, result.RecordsCount);
            Assert.Equal(2, result.StagesCovered);
        }
    }
}
