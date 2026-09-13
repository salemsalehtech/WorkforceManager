using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات ترتيب العمال المخصص (WorkerManagementService.MoveWorkerAsync).
    /// نفس أسلوب StageOrderTests بالظبط — تبديل مع الجار وإعادة ترقيم
    /// الكل من 1، عشان الترتيب المعتمد في كل الشاشات يفضل نضيف مهما
    /// اتحرك.
    /// </summary>
    public class WorkerOrderTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        /// <summary>العمال الثلاثة المزروعين بترتيبهم الفعلي</summary>
        private async Task<List<Worker>> GetWorkersInOrderAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            return await db.Workers
                .OrderBy(w => w.SortOrder).ThenBy(w => w.Id)
                .AsNoTracking()
                .ToListAsync();
        }

        private Task<bool> MoveAsync(int workerId, bool up) =>
            _db.InScopeAsync<WorkerManagementService, bool>(service =>
                service.MoveWorkerAsync(workerId, up));

        // ---------------- الحركة الأساسية ----------------

        [Fact]
        public async Task MovingFirstWorkerDown_SwapsItWithTheNextOne()
        {
            var before = await GetWorkersInOrderAsync();
            Assert.Equal(TestDatabase.WorkerAhmedId, before[0].Id);
            Assert.Equal(TestDatabase.WorkerSaidId, before[1].Id);

            var moved = await MoveAsync(TestDatabase.WorkerAhmedId, up: false);
            Assert.True(moved);

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(TestDatabase.WorkerSaidId, after[0].Id);
            Assert.Equal(TestDatabase.WorkerAhmedId, after[1].Id);
        }

        [Fact]
        public async Task MovingLastWorkerUp_SwapsItWithThePreviousOne()
        {
            var before = await GetWorkersInOrderAsync();
            var lastId = before[^1].Id;
            var middleId = before[^2].Id;

            var moved = await MoveAsync(lastId, up: true);
            Assert.True(moved);

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(lastId, after[^2].Id);
            Assert.Equal(middleId, after[^1].Id);
        }

        // ---------------- حدود القائمة ----------------

        [Fact]
        public async Task MovingFirstWorkerUp_DoesNothing()
        {
            var before = await GetWorkersInOrderAsync();

            var moved = await MoveAsync(before[0].Id, up: true);
            Assert.False(moved);

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(before.Select(w => w.Id), after.Select(w => w.Id));
        }

        [Fact]
        public async Task MovingLastWorkerDown_DoesNothing()
        {
            var before = await GetWorkersInOrderAsync();

            var moved = await MoveAsync(before[^1].Id, up: false);
            Assert.False(moved);

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(before.Select(w => w.Id), after.Select(w => w.Id));
        }

        // ---------------- إعادة الترقيم ----------------

        [Fact]
        public async Task MovingAWorker_RenumbersEveryoneFromOne()
        {
            await MoveAsync(TestDatabase.WorkerAhmedId, up: false);

            var after = await GetWorkersInOrderAsync();

            // الترتيب بيبقى 1، 2، 3... من غير فجوات ولا تكرار
            Assert.Equal(Enumerable.Range(1, after.Count), after.Select(w => w.SortOrder));
        }

        [Fact]
        public async Task MovingBackAndForth_ReturnsToTheOriginalOrder()
        {
            var before = await GetWorkersInOrderAsync();

            await MoveAsync(TestDatabase.WorkerAhmedId, up: false);
            await MoveAsync(TestDatabase.WorkerAhmedId, up: true);

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(before.Select(w => w.Id), after.Select(w => w.Id));
        }

        [Fact]
        public async Task MovingAnUnknownWorker_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => MoveAsync(999_999, up: true));
        }

        // ---------------- الأثر الحقيقي: كل مكان في البرنامج بيقرا نفس الترتيب ----------------

        [Fact]
        public async Task AfterReorder_TheWorkerRepositoryReturnsTheNewOrder()
        {
            await MoveAsync(TestDatabase.WorkerAhmedId, up: false); // سعيد بقى الأول

            var workers = await _db.InScopeAsync<WorkforceManager.Core.Interfaces.IWorkerRepository,
                IReadOnlyList<Worker>>(repo => repo.GetAllWithSkillsAsync());

            Assert.Equal(TestDatabase.WorkerSaidId, workers[0].Id);
            Assert.Equal(TestDatabase.WorkerAhmedId, workers[1].Id);
        }

        [Fact]
        public async Task NewWorker_IsAppendedAtTheEndOfTheCustomOrder()
        {
            var maxBefore = (await GetWorkersInOrderAsync()).Max(w => w.SortOrder);

            var created = await _db.InScopeAsync<WorkerManagementService, Worker>(service =>
                service.CreateWorkerAsync("عامل جديد للاختبار"));

            Assert.Equal(maxBefore + 1, created.SortOrder);
        }

        // ---------------- الترتيب الكامل دفعة واحدة (سحب/كتابة رقم) ----------------

        [Fact]
        public async Task ReorderAsync_AppliesTheWholeSequenceInOneGo()
        {
            var before = await GetWorkersInOrderAsync();
            // نفس فكرة سحب آخر واحد لأول مكان — نقلة واحدة كبيرة
            var newOrder = new[] { before[2].Id, before[0].Id, before[1].Id };

            await _db.InScopeAsync<WorkerManagementService, object?>(async service =>
            {
                await service.ReorderAsync(newOrder);
                return null;
            });

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(newOrder, after.Select(w => w.Id));
            Assert.Equal(new[] { 1, 2, 3 }, after.Select(w => w.SortOrder));
        }

        [Fact]
        public async Task ReorderAsync_IgnoresUnknownIds()
        {
            var before = await GetWorkersInOrderAsync();

            await _db.InScopeAsync<WorkerManagementService, object?>(async service =>
            {
                // الـId الوهمي بياخد مكانه في القايمة بس مالوش عامل يتطبّق
                // عليه، فبيتجاهل من غير ما يوقع أو يلخبط باقي الترتيب النسبي
                await service.ReorderAsync(new[] { 999_999, before[0].Id, before[1].Id, before[2].Id });
                return null;
            });

            var after = await GetWorkersInOrderAsync();
            Assert.Equal(before.Select(w => w.Id), after.Select(w => w.Id));
        }
    }
}
