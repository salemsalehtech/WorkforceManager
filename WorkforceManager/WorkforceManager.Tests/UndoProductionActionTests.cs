using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// زرار "تراجع" (وCtrl+Z) في تبويب سجلات اليوم — بيرجّع آخر تصحيح
    /// قطع/نقل عامل أو حذف بالظبط للحالة اللي كانت قبله، من غير كلمة
    /// سر (شوف تعليق UndoEditAsync/UndoDeleteAsync).
    /// </summary>
    public class UndoProductionActionTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

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

        // ---------------- تراجع عن تصحيح قطع ----------------

        [Fact]
        public async Task UndoEdit_RestoresThePreviousPieceCount()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, newWorkerId: null, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoEditAsync(
                    recordId, TestDatabase.WorkerAhmedId, 100);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(recordId, record.Id);
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);
            Assert.Equal(100, record.PieceCount);
        }

        [Fact]
        public async Task UndoEdit_DoesNotRequireAPassword()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            await _db.SignInTestUserAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, "1234");

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, "1234", newWorkerId: null, confirmOverride: true);

            // مفيش كلمة سر متبعتة هنا خالص، ومفيش استثناء
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoEditAsync(
                    recordId, TestDatabase.WorkerAhmedId, 100);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(100, record.PieceCount);
        }

        // ---------------- تراجع عن نقل عامل ----------------

        [Fact]
        public async Task UndoEdit_ReversesAWorkerReassignment_MovingAttendanceBack()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 100, newWorkerId: TestDatabase.WorkerSaidId, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoEditAsync(
                    recordId, TestDatabase.WorkerAhmedId, 100);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);

            using var check = _db.CreateScope();
            var attendanceRepo = _db.GetService<IAttendanceRepository>(check);
            Assert.NotNull(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));
            Assert.Null(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerSaidId, Today));
        }

        [Fact]
        public async Task UndoEdit_OnAClosedDay_IsRefused()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, newWorkerId: null, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Today);

            using (var scope = _db.CreateScope())
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    _db.GetService<WorkdayCalculationService>(scope).UndoEditAsync(
                        recordId, TestDatabase.WorkerAhmedId, 100));
                Assert.NotEmpty(ex.Message);
            }

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(150, record.PieceCount); // لسه على القيمة بعد التعديل، مفيش تراجع حصل
        }

        [Fact]
        public async Task UndoEdit_IsLoggedWithADistinctEventType()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(
                    recordId, 150, newWorkerId: null, confirmOverride: true);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoEditAsync(
                    recordId, TestDatabase.WorkerAhmedId, 100);

            Assert.Single(await EventsOfAsync(ActivityEventType.ProductionRecordUndone));
        }

        // ---------------- تراجع عن حذف ----------------

        [Fact]
        public async Task UndoDelete_RecreatesTheRecordWithTheSameData()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).DeleteProductionAsync(
                    recordId, "", "اتسجل بالغلط");

            Assert.Empty(await _db.GetProductionAsync());

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoDeleteAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id, Today,
                    100, piecesPerWorkdayAtEntry: 10, isRework: false);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, record.WorkerId);
            Assert.Equal(TestDatabase.ChainStage1Id, record.ProductionStageId);
            Assert.Equal(100, record.PieceCount);
        }

        [Fact]
        public async Task UndoDelete_GrantsAutomaticAttendance_IfTheWorkerHasNoneThatDay()
        {
            var recordId = await RecordAhmedOnChainAsync(100);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).DeleteProductionAsync(
                    recordId, "", "اتسجل بالغلط");

            using (var scope = _db.CreateScope())
            {
                var attendanceRepo = _db.GetService<IAttendanceRepository>(scope);
                Assert.Null(await attendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));
            }

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).UndoDeleteAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id, Today,
                    100, piecesPerWorkdayAtEntry: 10, isRework: false);

            using var check = _db.CreateScope();
            var checkAttendanceRepo = _db.GetService<IAttendanceRepository>(check);
            Assert.NotNull(await checkAttendanceRepo.GetByWorkerAndDateAsync(TestDatabase.WorkerAhmedId, Today));
        }
    }
}
