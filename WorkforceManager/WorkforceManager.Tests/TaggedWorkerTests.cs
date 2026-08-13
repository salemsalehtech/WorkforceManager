using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تاج "رص/تدريب" على مرحلة في رحلة الإنتاج اليومي: عامل بالساعة
    /// (HourlyRole != null) يتحط عليه تاج لليوم بس — بلا قطع وبلا شرط
    /// تأهيل (WorkerSkill)، وبيتسجّل حاضر بيومية شيفت عادي (1) تلقائيًا.
    /// منفصل تمامًا عن أنصبة العمال بالقطعة (FlowShareDto).
    ///
    /// خط الشنطة: 1.قص → 2.خياطة → 3.تشطيب. العاملة المزروعة
    /// (WorkerMonaHourlyId) دورها Racking، ومالهاش أي WorkerSkill.
    /// </summary>
    public class TaggedWorkerTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;

        private async Task<FlowSaveResultDto> RecordAsync(
            IReadOnlyList<FlowTaggedWorkerDto> taggedWorkers,
            int stageId = TestDatabase.BagStage1Id,
            int pieces = 100,
            int workerId = TestDatabase.WorkerAhmedId)
        {
            var range = new FlowRangeDto { FromStageId = stageId, ToStageId = stageId, PieceCount = pieces };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = stageId, WorkerId = workerId, PieceCount = pieces }
            };

            using var scope = _db.CreateScope();
            return await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, Day1, new[] { range }, shares,
                taggedWorkers: taggedWorkers, confirmOverride: true);
        }

        [Fact]
        public async Task TaggingAnHourlyWorker_RecordsPresenceAndAFullWorkday_WithNoPieceCount()
        {
            await RecordAsync(new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

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
        public async Task TaggedWorker_NeverAppearsInDailyProductionRecords()
        {
            // العاملة متحطة تاج بس، مش في shares خالص — ده أساس الفصل
            // عن أنصبة العمال بالقطعة
            await RecordAsync(new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            var records = await _db.GetProductionAsync();
            Assert.DoesNotContain(records, r => r.WorkerId == TestDatabase.WorkerMonaHourlyId);
        }

        [Fact]
        public async Task TaggedWorker_DoesNotNeedAnyWorkerSkillOnTheStage()
        {
            // WorkerMonaHourlyId مالهاش أي WorkerSkill (عاملة بالساعة) —
            // التسجيل لازم يعدي عادي بلا استثناء "غير مؤهل"
            var result = await RecordAsync(new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            Assert.True(result.RecordsCount > 0);
        }

        [Fact]
        public async Task TaggingOnAStageFromAnotherProduct_Throws()
        {
            // RingStage1Id تبع منتج "دبلة" — مش من مراحل "شنطة" اللي بنسجل عليها
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RecordAsync(new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = TestDatabase.RingStage1Id, WorkerId = TestDatabase.WorkerMonaHourlyId }
            }));

            Assert.Contains("مش من مراحل المنتج المحدد", ex.Message);
        }

        [Fact]
        public async Task RecordingProduction_WithNoTaggedWorkers_SavesWithoutAnyProblem()
        {
            var result = await RecordAsync(Array.Empty<FlowTaggedWorkerDto>());
            Assert.True(result.RecordsCount > 0);
        }

        [Fact]
        public async Task TaggedWorkerAndRackingWorker_BothGetMarkedPresent_InTheSameFlow()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .SetRackingWorkerAsync(TestDatabase.ProductBagId, TestDatabase.WorkerMonaHourlyId);

            // نفس العاملة متحطة عليها تاج وهي كمان عامل الرص الثابت للمنتج —
            // نفس نداء RecordHourlyWorkAsync هيتنادى مرتين (Upsert)، فمفيش تعارض
            await RecordAsync(new[]
            {
                new FlowTaggedWorkerDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerMonaHourlyId }
            });

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);
            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h =>
                h.WorkerId == TestDatabase.WorkerMonaHourlyId && h.Date == Day1.Date);
            Assert.NotNull(log);
            Assert.Equal(1.0m, log!.WorkdaysCredited);
        }
    }
}
