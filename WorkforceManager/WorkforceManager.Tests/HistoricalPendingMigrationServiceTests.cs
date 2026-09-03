using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// ترحيل الشغل الواقف القديم لأرصدة أولية — مرة واحدة، تقريبي بتاريخ
    /// الترحيل نفسه (شوف تعليق HistoricalPendingMigrationService).
    /// </summary>
    public class HistoricalPendingMigrationServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(int stageId, int pieces)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, stageId, pieces, Day, confirmOverride: true);
        }

        [Fact]
        public async Task A_product_with_no_pending_work_creates_no_balances()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<HistoricalPendingMigrationService>(scope).RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            Assert.Equal(0, await appDb.InitialBalances.CountAsync());
        }

        [Fact]
        public async Task A_single_boundary_with_positive_pending_creates_exactly_one_balance_and_range_to_the_lines_last_stage()
        {
            // منتج دبلة: مرحلتين بس — حد فاصل واحد ممكن
            await RecordAsync(TestDatabase.RingStage1Id, 1000);
            await RecordAsync(TestDatabase.RingStage2Id, 600); // 400 واقفة قدام RingStage2Id

            using var scope = _db.CreateScope();
            await _db.GetService<HistoricalPendingMigrationService>(scope).RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            var balance = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .SingleAsync(b => b.ProductId == TestDatabase.ProductRingId);

            Assert.Equal(400, balance.Quantity);
            Assert.Equal(InitialBalanceSource.Migrated, balance.Source);
            // تاريخ الترحيل نفسه (اليوم الحقيقي وقت التشغيل)، مش تاريخ
            // التسجيل الأصلي (TestDatabase.Today) — القاعدة المعتمدة:
            // مفيش تاريخ حقيقي محفوظ نقدر نرجعله، شوف تعليق الكلاس
            Assert.Equal(DateTime.Today, balance.OriginalDate);

            var range = Assert.Single(balance.Ranges);
            Assert.Equal(TestDatabase.RingStage2Id, range.FromStageId);
            Assert.Equal(TestDatabase.RingStage2Id, range.ToStageId);
        }

        [Fact]
        public async Task Multiple_boundaries_on_the_same_product_each_get_their_own_balance()
        {
            // منتج شنطة: 3 مراحل — حدّين فاصلين. قص=1000، خياطة=600
            // (400 واقفة قدام خياطة)، تشطيب=0 (600 واقفة قدام تشطيب)
            await RecordAsync(TestDatabase.BagStage1Id, 1000);
            await RecordAsync(TestDatabase.BagStage2Id, 600);

            using var scope = _db.CreateScope();
            await _db.GetService<HistoricalPendingMigrationService>(scope).RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            var balances = await appDb.InitialBalances
                .Where(b => b.ProductId == TestDatabase.ProductBagId)
                .ToListAsync();

            Assert.Equal(2, balances.Count);
            Assert.Contains(balances, b => b.Quantity == 400);
            Assert.Contains(balances, b => b.Quantity == 600);
        }

        [Fact]
        public async Task Running_the_migration_twice_is_a_no_op()
        {
            await RecordAsync(TestDatabase.RingStage1Id, 1000);
            await RecordAsync(TestDatabase.RingStage2Id, 600);

            using var scope = _db.CreateScope();
            var service = _db.GetService<HistoricalPendingMigrationService>(scope);
            await service.RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            var countAfterFirst = await appDb.InitialBalances.CountAsync();
            Assert.True(countAfterFirst > 0);

            await service.RunOnceAsync();
            Assert.Equal(countAfterFirst, await appDb.InitialBalances.CountAsync());
        }

        [Fact]
        public async Task A_boundary_with_zero_or_negative_pending_creates_nothing()
        {
            // نفس الرقم على الاتنين — مفيش واقف
            await RecordAsync(TestDatabase.RingStage1Id, 500);
            await RecordAsync(TestDatabase.RingStage2Id, 500);

            using var scope = _db.CreateScope();
            await _db.GetService<HistoricalPendingMigrationService>(scope).RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            Assert.Equal(0, await appDb.InitialBalances.CountAsync());
        }

        [Fact]
        public async Task Migrated_balances_are_withdrawable_through_the_normal_flow_afterward()
        {
            await _db.SignInTestUserAsync();
            await RecordAsync(TestDatabase.RingStage1Id, 1000);
            await RecordAsync(TestDatabase.RingStage2Id, 600);

            using var scope = _db.CreateScope();
            await _db.GetService<HistoricalPendingMigrationService>(scope).RunOnceAsync();

            var appDb = _db.GetService<AppDbContext>(scope);
            var migrated = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .SingleAsync(b => b.ProductId == TestDatabase.ProductRingId);
            var range = migrated.Ranges.Single();

            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.RingStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 400 }
            };
            // تاريخ السحب لازم يكون بعد أو يساوي balance.OriginalDate، وده
            // بقى تاريخ الترحيل الحقيقي (DateTime.Today) مش TestDatabase.Today
            var result = await _db.GetService<InitialBalanceService>(scope).WithdrawAsync(
                migrated.Id,
                new[] { new InitialBalanceRangeWithdrawalDto { RangeId = range.Id, PieceCount = 400 } },
                shares, DateTime.Today.AddDays(1), confirmOverride: true);

            Assert.Single(result.CreatedRows);

            var updated = await _db.InScopeAsync<InitialBalanceService, InitialBalanceDto?>(s => s.GetByIdAsync(migrated.Id));
            Assert.NotNull(updated);
            Assert.Equal(InitialBalanceStatus.Completed, updated.Status);
        }
    }
}
