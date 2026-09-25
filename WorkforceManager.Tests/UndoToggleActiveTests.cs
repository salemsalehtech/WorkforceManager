using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// "تراجع" على إيقاف/تشغيل (العامل، الحساب الإداري، المنتج، المرحلة) بينادي
    /// العكس الحقيقي في الخدمة — الاختبارات دي بتأكد إن الصف بيرجع **زي ما كان
    /// بالظبط** (كل أعمدته، مش IsActive بس)، يعني تراجع حقيقي مش شكل.
    /// </summary>
    public class UndoToggleActiveTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Worker_DeactivateThenUndo_RestoresExactRow()
        {
            var before = await SnapshotAsync<Worker>(TestDatabase.WorkerAhmedId);

            await WithWorkers(s => s.DeactivateWorkerAsync(TestDatabase.WorkerAhmedId));
            Assert.False((bool)(await SnapshotAsync<Worker>(TestDatabase.WorkerAhmedId))[nameof(Worker.IsActive)]!);

            await WithWorkers(s => s.ReactivateWorkerAsync(TestDatabase.WorkerAhmedId));

            Assert.Equal(before, await SnapshotAsync<Worker>(TestDatabase.WorkerAhmedId));
        }

        [Fact]
        public async Task Worker_ReactivateThenUndo_RestoresExactRow()
        {
            await WithWorkers(s => s.DeactivateWorkerAsync(TestDatabase.WorkerSaidId));
            var before = await SnapshotAsync<Worker>(TestDatabase.WorkerSaidId);

            await WithWorkers(s => s.ReactivateWorkerAsync(TestDatabase.WorkerSaidId));
            await WithWorkers(s => s.DeactivateWorkerAsync(TestDatabase.WorkerSaidId));

            Assert.Equal(before, await SnapshotAsync<Worker>(TestDatabase.WorkerSaidId));
        }

        [Fact]
        public async Task Product_DeactivateThenUndo_RestoresExactRow()
        {
            var before = await SnapshotAsync<Product>(TestDatabase.ProductRingId);

            await WithProducts(s => s.DeactivateProductAsync(TestDatabase.ProductRingId));
            await WithProducts(s => s.ReactivateProductAsync(TestDatabase.ProductRingId));

            Assert.Equal(before, await SnapshotAsync<Product>(TestDatabase.ProductRingId));
        }

        [Fact]
        public async Task Stage_DeactivateThenUndo_RestoresExactRow()
        {
            var before = await SnapshotAsync<ProductionStage>(TestDatabase.RingStage1Id);

            await WithProducts(s => s.DeactivateStageAsync(TestDatabase.RingStage1Id));
            await WithProducts(s => s.ReactivateStageAsync(TestDatabase.RingStage1Id));

            Assert.Equal(before, await SnapshotAsync<ProductionStage>(TestDatabase.RingStage1Id));
        }

        private async Task WithWorkers(Func<WorkerManagementService, Task> act)
        {
            using var scope = _db.CreateScope();
            await act(_db.GetService<WorkerManagementService>(scope));
        }

        private async Task WithProducts(Func<ProductManagementService, Task> act)
        {
            using var scope = _db.CreateScope();
            await act(_db.GetService<ProductManagementService>(scope));
        }

        /// <summary>كل الأعمدة البسيطة للصف زي ما هي في قاعدة البيانات (من غير علاقات)</summary>
        private async Task<Dictionary<string, object?>> SnapshotAsync<T>(int id) where T : class
        {
            using var scope = _db.CreateScope();
            var context = _db.GetService<AppDbContext>(scope);
            var entity = await context.Set<T>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(e => EF.Property<int>(e, "Id") == id);

            return context.Entry(entity).Properties
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        }
    }
}
