using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// أي نطاق مُقدَّم في رحلة إنتاج ومش وصل لآخر مرحلة في الخط بيتحول
    /// تلقائيًا (من غير سؤال المستخدم) لرصيد أولي — شوف التعليق في
    /// ProductionFlowService.RecordFlowAsync عن التحويل التلقائي وعن
    /// postWriteHook.
    /// </summary>
    public class ProductionFlowAutoBalanceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        [Fact]
        public async Task A_range_that_stops_before_the_lines_last_stage_automatically_creates_an_initial_balance()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 40 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 }
            };

            var result = await service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);

            Assert.Single(result.IncompleteRanges);

            var appDb = _db.GetService<AppDbContext>(scope);
            var balance = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .SingleAsync(b => b.ProductId == TestDatabase.ProductBagId);

            Assert.Equal(40, balance.Quantity);
            Assert.Equal(Day, balance.OriginalDate);
            var range = Assert.Single(balance.Ranges);
            // النطاق التلقائي بيبدأ من **بعد** المرحلة اللي النطاق الناقص
            // وصلها (BagStage2Id) — مش منها، عشان إنتاجها الفعلي اتسجل
            // خلاص في الحفظة الأصلية ومايتكررش لو الرصيد ده اتسحب بعدين
            Assert.Equal(TestDatabase.BagStage3Id, range.FromStageId);
            Assert.Equal(TestDatabase.BagStage3Id, range.ToStageId);
        }

        [Fact]
        public async Task A_range_that_reaches_the_lines_last_stage_creates_no_balance()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 40 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 }
            };

            var result = await service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);

            Assert.Empty(result.IncompleteRanges);

            var appDb = _db.GetService<AppDbContext>(scope);
            Assert.False(await appDb.InitialBalances.AnyAsync(b => b.ProductId == TestDatabase.ProductBagId));
        }

        [Fact]
        public async Task Multiple_incomplete_ranges_in_one_submission_each_create_their_own_balance()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            // نطاقين منفصلين، ولا واحد فيهم بيوصل BagStage3Id (آخر مرحلة)
            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 20 },
                new FlowRangeDto { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 30 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 20 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 30 }
            };

            var result = await service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);

            Assert.Equal(2, result.IncompleteRanges.Count);

            var appDb = _db.GetService<AppDbContext>(scope);
            var balances = await appDb.InitialBalances.Where(b => b.ProductId == TestDatabase.ProductBagId).ToListAsync();
            Assert.Equal(2, balances.Count);
            Assert.Contains(balances, b => b.Quantity == 20);
            Assert.Contains(balances, b => b.Quantity == 30);
        }

        [Fact]
        public async Task A_postWriteHook_failure_rolls_back_the_whole_save()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 40 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 }
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true,
                    postWriteHook: _ => throw new InvalidOperationException("فشل مقصود للاختبار")));

            using var check = _db.CreateScope();
            var appDb = _db.GetService<AppDbContext>(check);
            Assert.False(await appDb.DailyProductions.AnyAsync(dp => dp.Date == Day));
        }

        [Fact]
        public async Task CreatedRows_reports_one_entry_per_daily_production_row_written()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 40 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 40 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 40 }
            };

            var result = await service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);

            Assert.Equal(2, result.CreatedRows.Count);
            Assert.All(result.CreatedRows, r => Assert.True(r.DailyProductionId > 0));
            Assert.All(result.CreatedRows, r => Assert.Equal(0, r.SubmittedRangeIndex));
        }
    }
}
