using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// "اتسجّل على عامل غلط بالغلط" — نقل سجل إنتاج محفوظ بالكامل من
    /// عامل لعامل تاني (نفس المرحلة والتاريخ)، مش مجرد تعديل قطع.
    ///
    /// القواعد المختبرة هنا:
    ///   • اليومية بتتحول من العامل القديم للجديد على نفس السجل (مش سجل جديد).
    ///   • العامل الجديد لازم يكون مؤهل فعلاً على المرحلة دي.
    ///   • نفس قاعدة تعارض/تكرار التكليف (WorkerAssignmentGuard) بتتفحص
    ///     على العامل الجديد بالظبط زي أي تسجيل عادي.
    ///   • كلمة سر العمليات لازم تتحقق (نفس بوابة تصحيح القطع).
    ///   • الحضور التلقائي بيتحول معاه: يتشال من القديم لو مالوش حاجة
    ///     تانية نفس اليوم، ويتحط للجديد لو مالوش سجل حضور أصلاً.
    ///   • الحدث بيتسجل في سجل العمليات بنوع مستقل وواضح.
    /// </summary>
    public class ProductionWorkerReassignmentTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        private async Task SetPasswordAsync(string password = "1234")
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, password);
        }

        private async Task<int> RecordAhmedOnChainAsync(int pieces = 100)
        {
            using var scope = _db.CreateScope();
            var record = await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id, pieces, Today, confirmOverride: true);
            return record.Id;
        }

        private async Task<List<Core.Models.ActivityEvent>> EventsOfAsync(ActivityEventType type)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<AppDbContext>(scope).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == type).ToListAsync();
        }

        // ---------------- الحالة الأساسية: نقل اليومية ----------------

        [Fact]
        public async Task ReassigningWorker_MovesTheRecordToTheNewWorker_KeepingItAsOneRecord()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            var records = await _db.GetProductionAsync();
            var record = Assert.Single(records);
            Assert.Equal(recordId, record.Id);
            Assert.Equal(TestDatabase.WorkerSaidId, record.WorkerId);
            Assert.Equal(100, record.PieceCount);
        }

        [Fact]
        public async Task ReassigningWorker_CanChangePiecesInTheSameOperation()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerSaidId, record.WorkerId);
            Assert.Equal(150, record.PieceCount);
        }

        [Fact]
        public async Task PassingTheSameWorkerId_IsTreatedAsAPlainPieceEdit_NotAReassignment()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, newWorkerId: TestDatabase.WorkerAhmedId, confirmOverride: true);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);
            Assert.Equal(150, record.PieceCount);

            Assert.Single(await EventsOfAsync(ActivityEventType.ProductionPiecesEdited));
            Assert.Empty(await EventsOfAsync(ActivityEventType.ProductionWorkerReassigned));
        }

        // ---------------- بوابة كلمة السر ----------------

        [Fact]
        public async Task ReassigningWithAWrongPassword_IsRefused_AndNothingChanges()
        {
            var recordId = await RecordAhmedOnChainAsync(100);
            await SetPasswordAsync();

            using (var scope = _db.CreateScope())
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                        recordId, 100, "غلط", newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true));

                Assert.NotEmpty(ex.Message);
            }

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);
        }

        // ---------------- تأهيل العامل الجديد ----------------

        [Fact]
        public async Task ReassigningToAnUnqualifiedWorker_IsRejected()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
            {
                // منى عاملة بالساعة، مالهاش أي مهارة على أي مرحلة بالقطعة
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                        recordId, 100, newWorkerId: TestDatabase.WorkerMonaHourlyId, confirmOverride: true));

                Assert.Contains("مؤهل", ex.Message);
            }

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);
        }

        // ---------------- تعارض/تكرار التكليف ----------------

        [Fact]
        public async Task ReassigningToAWorkerAssignedElsewhereSameDay_AllowsTheTransferWithoutConfirmation()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            // سعيد أصلاً شغال على منتج/مرحلة تانية النهارده — النقل نفسه
            // بيحلّ التعارض بدل ما يرفض.
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerSaidId, TestDatabase.RingStage1Id, 50, Today, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: false);

            var chainRecord = (await _db.GetProductionAsync())
                .Single(r => r.ProductionStageId == TestDatabase.ChainStage1Id);
            Assert.Equal(TestDatabase.WorkerSaidId, chainRecord.WorkerId);
        }

        [Fact]
        public async Task ReassigningToAWorkerAlreadyOnTheSameStageSameDay_StillTransfersTheRecord()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            // سعيد أصلاً عنده سجل على نفس المرحلة النهارده — النقل فعليًا
            // بيستبدل العامل بدل ما يرفض التسجيل نفسه.
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerSaidId, TestDatabase.ChainStage1Id, 30, Today, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            var record = (await _db.GetProductionAsync()).Single(r => r.Id == recordId);
            Assert.Equal(TestDatabase.WorkerSaidId, record.WorkerId);
        }

        [Fact]
        public async Task ReassigningWorkerInsideAnExistingTransaction_DoesNotThrowNestedTransactionException()
        {
            var recordId = await RecordAhmedOnChainAsync(100);
            using (var scope = _db.CreateScope())
            {
                var unitOfWork = _db.GetService<IUnitOfWork>(scope);
                var service = _db.GetService<WorkdayCalculationService>(scope);

                await using var outerTx = await unitOfWork.BeginWriteTransactionAsync();
                await service.UpdateProductionAsync(recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: false);
                await outerTx.CommitAsync();
            }

            var record = (await _db.GetProductionAsync()).Single(r => r.Id == recordId);
            Assert.Equal(TestDatabase.WorkerSaidId, record.WorkerId);
        }

        // ---------------- الحضور التلقائي ----------------

        [Fact]
        public async Task ReassigningWorker_RemovesOldWorkersAutoAttendance_AndGrantsItToTheNewWorker()
        {
            int recordId;
            using (var scope = _db.CreateScope())
            {
                var result = await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductChainId, Today,
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.ChainStage1Id,
                            ToStageId = TestDatabase.ChainStage1Id, PieceCount = 100
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.ChainStage1Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100
                        }
                    });

                Assert.Equal(1, result.AttendanceMarkedCount);
                recordId = (await _db.GetProductionAsync()).Single().Id;
            }

            using (var scope = _db.CreateScope())
            {
                var attendanceRepo = _db.GetService<IAttendanceRepository>(scope);
                Assert.NotNull(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));
                Assert.Null(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerSaidId, Today));
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            using (var scope = _db.CreateScope())
            {
                var attendanceRepo = _db.GetService<IAttendanceRepository>(scope);

                // أحمد بقى من غير أي إنتاج تاني النهارده — حضوره التلقائي بيتشال
                Assert.Null(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));

                // سعيد ماكانش له حضور مسجل — بياخد حضور تلقائي بعد النقل
                Assert.NotNull(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerSaidId, Today));
            }
        }

        [Fact]
        public async Task ReassigningWorker_KeepsTheOldWorkersAttendance_IfHeHasOtherProductionThatDay()
        {
            int chainRecordId;
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductChainId, Today,
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.ChainStage1Id,
                            ToStageId = TestDatabase.ChainStage1Id, PieceCount = 100
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.ChainStage1Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100
                        }
                    });

                chainRecordId = (await _db.GetProductionAsync()).Single().Id;

                // أحمد كمان شغال على منتج تاني النهارده — تعارض متوقع، بيتأكد عليه
                await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductRingId, Today,
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.RingStage1Id,
                            ToStageId = TestDatabase.RingStage1Id, PieceCount = 40
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage1Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40
                        }
                    },
                    confirmOverride: true);
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    chainRecordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            using var check = _db.CreateScope();
            var attendanceRepo = _db.GetService<IAttendanceRepository>(check);

            // أحمد لسه له إنتاج على الدبلة نفس اليوم — حضوره التلقائي يفضل موجود
            Assert.NotNull(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));
            Assert.NotNull(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerSaidId, Today));
        }

        // ---------------- سجل العمليات ----------------

        [Fact]
        public async Task ReassigningWorker_IsLoggedWithADistinctEventType_IncludingBothWorkersAndTheReason()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true,
                    reason: "اتسجل غلط على أحمد");

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.ProductionWorkerReassigned));
            Assert.Contains("أحمد", logged.EntityName);
            Assert.Contains("سعيد", logged.EntityName);
            Assert.Equal("اتسجل غلط على أحمد", logged.Reason);
            Assert.Empty(await EventsOfAsync(ActivityEventType.ProductionPiecesEdited));
        }
    }
}
