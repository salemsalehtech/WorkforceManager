using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// إعادة العمل من طرف لطرف على قاعدة بيانات حقيقية.
    ///
    /// الميزة كلها بتقف على معادلة واحدة: **القطع دي بتتحسب في يومية
    /// العامل وأجره، ومابتعدّش في إنتاج الخط**. الاختبارات هنا بتحرس
    /// النصين مع بعض — لأن لو واحد منهم اتكسر، الميزة بقت غلط بدل ما
    /// تبقى ناقصة: يا العامل بيشتغل ببلاش، يا المصنع بيقول إنه أنتج
    /// حاجة عمرها ما خرجت من الخط.
    /// </summary>
    public class ReworkProductionTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        /// <summary>تسجيل عادي: نطاق على مرحلة واحدة بعامل واحد</summary>
        private Task<FlowSaveResultDto> RecordNormalAsync(int stageId, int workerId, int pieces = 100) =>
            _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    TestDatabase.ProductRingId,
                    TestDatabase.Today,
                    new[] { new FlowRangeDto { FromStageId = stageId, ToStageId = stageId, PieceCount = pieces } },
                    new[] { new FlowShareDto { ProductionStageId = stageId, WorkerId = workerId, PieceCount = pieces } }));

        /// <summary>
        /// إعادة عمل لوحدها: العامل بيصلّح شغل خلص، فمفيش نطاق إنتاج
        /// جديد وراها — ودي بالظبط الحالة اللي كانت مرفوضة قبل الميزة.
        /// </summary>
        private Task<FlowSaveResultDto> RecordReworkAsync(int stageId, int workerId, int pieces = 20) =>
            _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    TestDatabase.ProductRingId,
                    TestDatabase.Today,
                    // النطاق لازم يبقى فيه مرحلة واحدة على الأقل (شرط الخدمة)،
                    // فبنحط النطاق على مرحلة تانية بعاملها، والإعادة برّه أي نطاق
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.RingStage2Id,
                            ToStageId = TestDatabase.RingStage2Id,
                            PieceCount = 50
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage2Id,
                            WorkerId = TestDatabase.WorkerSaidId,
                            PieceCount = 50
                        },
                        new FlowShareDto
                        {
                            ProductionStageId = stageId, WorkerId = workerId,
                            PieceCount = pieces, IsRework = true
                        }
                    }));

        private Task<IReadOnlyDictionary<int, int>> StageTotalsAsync() =>
            _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(TestDatabase.Today));

        // ---------------- القاعدة: نفس العامل على نفس المرحلة ----------------

        [Fact]
        public async Task SameWorkerSameStageSameDay_MarkedAsRework_IsAllowed()
        {
            await RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

            await RecordReworkAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 20);

            var onStage1 = (await _db.GetProductionAsync())
                .Where(r => r.ProductionStageId == TestDatabase.RingStage1Id)
                .ToList();

            Assert.Equal(2, onStage1.Count);
            Assert.Single(onStage1, r => r.IsRework && r.PieceCount == 20);
        }

        [Fact]
        public async Task SameWorkerSameStageSameDay_WithoutTheReworkFlag_IsStillBlocked()
        {
            await RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId);

            // من غير العلم، دي لسه تسجيل مكرر بالغلط — وممنوع زي ما كان
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId));

            Assert.Contains("مسجلة بالفعل", ex.Message);
        }

        // ---------------- النص التاني: العامل بياخد أجره ----------------

        [Fact]
        public async Task ReworkPieces_CountTowardTheWorkersWorkdays()
        {
            await RecordReworkAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 20);

            var row = Assert.Single(await _db.GetProductionAsync(), r => r.IsRework);

            Assert.Equal(20, row.PieceCount);
            Assert.True(row.PiecesPerWorkdayAtEntry > 0);  // اليومية اتاخدت Snapshot زي أي سجل
            Assert.True(row.WorkdaysCompleted > 0);        // فبيتحسب له يومية وأجر عادي
        }

        // ---------------- النص الأول: الإنتاج مابيزيدش ----------------

        [Fact]
        public async Task ReworkPieces_DoNotRaiseTheStagesRealOutput()
        {
            await RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 100);

            var before = (await StageTotalsAsync())[TestDatabase.RingStage1Id];

            await RecordReworkAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 20);

            var after = (await StageTotalsAsync())[TestDatabase.RingStage1Id];

            Assert.Equal(100, before);
            Assert.Equal(before, after);
        }

        [Fact]
        public async Task AStageWithNothingButRework_ShowsZeroRealOutput()
        {
            // مفيش أي إنتاج عادي على المرحلة دي خالص — الإعادة بس
            await RecordReworkAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 500);

            var totals = await StageTotalsAsync();

            // لا سجل في الجدول الجديد، ولا الرجوع للحساب القديم بيلمّها
            Assert.False(totals.ContainsKey(TestDatabase.RingStage1Id));
        }

        [Fact]
        public async Task ReworkPieces_AreExcludedFromPendingWork()
        {
            // الشغل الواقف = اللي خلص المرحلة اللي قبلها وماعدّاش دي. لو
            // قطع الإعادة اتحسبت إنتاج، الرقم ده هيقل غلط والمستخدم هيفتكر
            // إن شغل واقف اتكمّل وهو لسه مكانه
            await RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 100);
            await RecordReworkAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 40);

            var pending = await _db.InScopeAsync<PendingWorkService, ProductPendingDto?>(
                svc => svc.GetForProductAsync(TestDatabase.ProductRingId, TestDatabase.Today));

            Assert.NotNull(pending);

            var stage2 = pending!.Stages.FirstOrDefault(s => s.StageId == TestDatabase.RingStage2Id);
            Assert.NotNull(stage2);

            // 100 خلصوا المرحلة الأولى، 50 بس خلصوا التانية → 50 واقفين.
            // قطع الإعادة (40) مالهاش أي أثر على الرقم ده
            Assert.Equal(50, stage2!.PendingPieces);
        }

        // ---------------- الحفظ الجزئي بيقلّل الشغل الواقف فعلاً ----------------

        [Fact]
        public async Task SavingOnlyTheStaffedStage_ActuallyReducesPendingWork()
        {
            // ده اللي الشاشة بتعمله بعد ما تقطّع النطاق: بتبعت المرحلة
            // اللي عليها عمال بس. المهم إن الشغل الواقف يقلّ فعلاً —
            // ده اللي المستخدم بيقيس بيه إن شغله اتحفظ
            await RecordNormalAsync(TestDatabase.RingStage1Id, TestDatabase.WorkerAhmedId, pieces: 100);

            var before = await _db.InScopeAsync<PendingWorkService, ProductPendingDto?>(
                svc => svc.GetForProductAsync(TestDatabase.ProductRingId, TestDatabase.Today));
            Assert.Equal(100, before!.Stages.Single(s => s.StageId == TestDatabase.RingStage2Id).PendingPieces);

            // المرحلة التانية اتكمّلت بعدين (يوم تاني أو نفس اليوم) بعامل تاني
            await RecordNormalAsync(TestDatabase.RingStage2Id, TestDatabase.WorkerSaidId, pieces: 100);

            var after = await _db.InScopeAsync<PendingWorkService, ProductPendingDto?>(
                svc => svc.GetForProductAsync(TestDatabase.ProductRingId, TestDatabase.Today));

            Assert.False(after!.HasPending); // الواقف اختفى خالص
        }

        // ---------------- الحفظ لسه بيرفض اللي المفروض يرفضه ----------------

        [Fact]
        public async Task AStageCoveredByARange_WithOnlyAReworkWorker_IsStillRejected()
        {
            // النطاق بيقول إن القطع خرجت من المرحلة فعلاً — والمصلّح مش
            // هو اللي خرّجها، فالمرحلة لسه "عليها إنتاج ومفيش عامل"
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                    service.RecordFlowAsync(
                        TestDatabase.ProductRingId,
                        TestDatabase.Today,
                        new[]
                        {
                            new FlowRangeDto
                            {
                                FromStageId = TestDatabase.RingStage1Id,
                                ToStageId = TestDatabase.RingStage1Id,
                                PieceCount = 100
                            }
                        },
                        new[]
                        {
                            new FlowShareDto
                            {
                                ProductionStageId = TestDatabase.RingStage1Id,
                                WorkerId = TestDatabase.WorkerAhmedId,
                                PieceCount = 100, IsRework = true
                            }
                        })));

            Assert.Contains("مفيش عامل متوزع عليها", ex.Message);
            Assert.Empty(await _db.GetProductionAsync());
        }
    }
}
