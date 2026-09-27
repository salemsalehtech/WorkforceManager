using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// عامل غير مؤهل لمرحلة، اتحدد ليها من رحلة الإنتاج أو من تصحيح سجل —
    /// القرار اتغيّر من "رفض الحفظ" لـ"إضافة المهارة تلقائيًا بنجمة واحدة"
    /// (شوف ProductionFlowService.RecordFlowAsync وWorkdayCalculationService.
    /// UpdateProductionAsync). التقييم اليدوي مش بيتلمس خالص — شوف آخر اختبار.
    /// </summary>
    public class SkillAutoAddTests : IDisposable
    {
        private const int UnskilledWorkerId = 900;

        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task SeedUnskilledWorkerAsync()
        {
            using var scope = _db.CreateScope();
            var context = _db.GetService<AppDbContext>(scope);
            context.Workers.Add(new Worker
            {
                Id = UnskilledWorkerId, FullName = "زياد الجديد", IsActive = true, DailyWageEgp = 200m
            });
            await context.SaveChangesAsync();
        }

        private async Task<WorkerSkill?> GetSkillAsync(int workerId, int stageId)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<AppDbContext>(scope).WorkerSkills
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.WorkerId == workerId && s.ProductionStageId == stageId);
        }

        [Fact]
        public async Task RecordFlowAsync_WithAnUnqualifiedWorker_NoLongerThrows_AndAutoAddsTheSkill()
        {
            await SeedUnskilledWorkerAsync();

            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 30 } };
            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = UnskilledWorkerId, PieceCount = 30 } };

            // متكانش هترمي InvalidOperationException زي الأول
            await service.RecordFlowAsync(TestDatabase.ProductChainId, Day, ranges, shares, confirmOverride: true);

            var skill = await GetSkillAsync(UnskilledWorkerId, TestDatabase.ChainStage1Id);
            Assert.NotNull(skill);
            Assert.Equal(1, skill!.Stars);
            Assert.True(skill.IsAutoAdded);
        }

        [Fact]
        public async Task RecordFlowAsync_SelectingTheSameUnqualifiedWorkerTwice_NeverDuplicatesTheSkill()
        {
            await SeedUnskilledWorkerAsync();

            var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 30 } };
            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = UnskilledWorkerId, PieceCount = 30 } };

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(scope)
                    .RecordFlowAsync(TestDatabase.ProductChainId, Day, ranges, shares, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(scope)
                    .RecordFlowAsync(TestDatabase.ProductChainId, Day.AddDays(1), ranges, shares, confirmOverride: true);

            using var checkScope = _db.CreateScope();
            var count = await _db.GetService<AppDbContext>(checkScope).WorkerSkills
                .AsNoTracking()
                .CountAsync(s => s.WorkerId == UnskilledWorkerId && s.ProductionStageId == TestDatabase.ChainStage1Id);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task RecordFlowAsync_AutoAddingASkill_LogsExactlyOneActivityEvent()
        {
            await SeedUnskilledWorkerAsync();

            using var scope = _db.CreateScope();
            var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 30 } };
            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = UnskilledWorkerId, PieceCount = 30 } };

            await _db.GetService<ProductionFlowService>(scope)
                .RecordFlowAsync(TestDatabase.ProductChainId, Day, ranges, shares, confirmOverride: true);

            var events = await _db.GetService<AppDbContext>(scope).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == ActivityEventType.SkillAutoAdjusted).ToListAsync();
            var logged = Assert.Single(events);
            Assert.Contains("0 → 1", logged.Details);
        }

        [Fact]
        public async Task UpdateProductionAsync_TransferringToAnUnqualifiedWorker_NoLongerThrows_AndAutoAddsTheSkill()
        {
            await SeedUnskilledWorkerAsync();

            // سجل موجود بالفعل لعامل مؤهل، هنقله لعامل غير مؤهل
            int recordId;
            using (var scope = _db.CreateScope())
            {
                var flow = _db.GetService<ProductionFlowService>(scope);
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 20 } };
                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 } };
                var result = await flow.RecordFlowAsync(TestDatabase.ProductChainId, Day, ranges, shares, confirmOverride: true);
                recordId = result.CreatedRows[0].DailyProductionId;
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .UpdateProductionAsync(recordId, 20, newWorkerId: UnskilledWorkerId, confirmOverride: true);

            var skill = await GetSkillAsync(UnskilledWorkerId, TestDatabase.ChainStage1Id);
            Assert.NotNull(skill);
            Assert.Equal(1, skill!.Stars);
            Assert.True(skill.IsAutoAdded);
        }

        [Fact]
        public async Task AutoGrowth_NeverTouchesAManuallySetSkillRating()
        {
            // نفس التأكيد اللي اتوافق عليه: عامل عنده مهارة يدوية (مش
            // تلقائية) من الأول (Ahmed مؤهل على كل المراحل بالبذر
            // الافتراضي) — الحفظ عليها متكرر مبيغيّرش IsAutoAdded ولا يلمسها
            using var scope = _db.CreateScope();
            var before = await GetSkillAsync(TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id);
            Assert.NotNull(before);
            Assert.False(before!.IsAutoAdded);

            var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 30 } };
            var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 30 } };
            await _db.GetService<ProductionFlowService>(scope)
                .RecordFlowAsync(TestDatabase.ProductChainId, Day, ranges, shares, confirmOverride: true);

            var after = await GetSkillAsync(TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id);
            Assert.False(after!.IsAutoAdded);
            Assert.Equal(before.Stars, after.Stars);
        }

        [Fact]
        public async Task AutoAddedSkill_GrowsAfterEnoughGoodSelections_AndLogsTheIncrease()
        {
            await SeedUnskilledWorkerAsync();

            // كوتة المرحلة 10 قطعة/يومية (شوف TestDatabase) — 10 قطعة في
            // 3 أيام مختلفة = أداء 1.0 بالظبط (نجوم جودة 3، بند 0.85)،
            // وعدد اختيارات 3 = بالظبط عتبة النجمة التانية (2⭐/3 اختيارات)
            for (var day = 0; day < 3; day++)
            {
                using var scope = _db.CreateScope();
                var ranges = new[] { new FlowRangeDto { FromStageId = TestDatabase.ChainStage1Id, ToStageId = TestDatabase.ChainStage1Id, PieceCount = 10 } };
                var shares = new[] { new FlowShareDto { ProductionStageId = TestDatabase.ChainStage1Id, WorkerId = UnskilledWorkerId, PieceCount = 10 } };
                await _db.GetService<ProductionFlowService>(scope)
                    .RecordFlowAsync(TestDatabase.ProductChainId, Day.AddDays(day), ranges, shares, confirmOverride: true);
            }

            var skill = await GetSkillAsync(UnskilledWorkerId, TestDatabase.ChainStage1Id);
            Assert.NotNull(skill);
            Assert.Equal(2, skill!.Stars); // 1★ الإضافة الأولى، بعدين نما لـ2★
            Assert.True(skill.IsAutoAdded);

            using var checkScope = _db.CreateScope();
            var events = await _db.GetService<AppDbContext>(checkScope).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == ActivityEventType.SkillAutoAdjusted).ToListAsync();
            Assert.Equal(2, events.Count); // "0 → 1" وقت الإضافة، و"1 → 2" وقت النمو
            Assert.Contains(events, e => e.Details!.Contains("1 → 2"));
        }
    }
}
