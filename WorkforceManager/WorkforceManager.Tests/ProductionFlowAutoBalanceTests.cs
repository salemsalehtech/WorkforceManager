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

        /// <summary>
        /// المثال الأصلي اللي الخوارزمية اتصمّمت عشانه: نطاق مبكر (5000)،
        /// نطاق نص (4500)، ونطاق بيوصل لآخر مرحلة (4000) — لازم يطلعوا
        /// فجوتين منفصلتين (500 عند حد 1→2، و500 عند حد 2→3(آخر مرحلة))،
        /// **مش** فجوة واحدة 1000 ولا فجوة 5000 كاملة (الباگ القديم كان بياخد
        /// كمية النطاق المبكر كاملة بدل الفرق الفعلي).
        /// </summary>
        [Fact]
        public async Task Multiple_stage_boundaries_with_gaps_each_create_their_own_correctly_sized_balance()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionFlowService>(scope);

            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 5000 },
                new FlowRangeDto { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 4500 },
                new FlowRangeDto { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 4000 }
            };
            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 5000 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 4500 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 4000 }
            };

            var result = await service.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);

            Assert.Equal(2, result.IncompleteRanges.Count);

            var appDb = _db.GetService<AppDbContext>(scope);
            var balances = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .Where(b => b.ProductId == TestDatabase.ProductBagId)
                .ToListAsync();

            Assert.Equal(2, balances.Count);
            Assert.All(balances, b => Assert.Equal(500, b.Quantity)); // مش 5000، ومش 1000 واحدة
            Assert.Contains(balances, b => b.Ranges.Single().FromStageId == TestDatabase.BagStage2Id);
            Assert.Contains(balances, b => b.Ranges.Single().FromStageId == TestDatabase.BagStage3Id);
            Assert.All(balances, b => Assert.Equal(TestDatabase.BagStage3Id, b.Ranges.Single().ToStageId));
        }

        /// <summary>
        /// نفس الفجوة (بين مرحلتين) بتتسجل عبر حفظتين منفصلتين لنفس اليوم —
        /// الحفظة التانية لازم تلاقي الفجوة متغطّية بالرصيد اللي الحفظة
        /// الأولى عملته، فمتعملش رصيد تاني مكرر لنفس القطع.
        /// </summary>
        [Fact]
        public async Task Second_save_does_not_duplicate_a_gap_an_earlier_save_already_balanced()
        {
            // حفظة 1: 100 قطعة على أول مرحلة بس → فجوة 100 عند حد 1→2،
            // بتتحول رصيد (من مرحلة 2 لآخر مرحلة، 100 قطعة)
            using (var scope1 = _db.CreateScope())
            {
                var service1 = _db.GetService<ProductionFlowService>(scope1);
                var ranges = new[]
                {
                    new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 100 }
                };
                var shares = new[]
                {
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 }
                };
                await service1.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);
            }

            // حفظة 2 (نفس اليوم): 10 قطع بس على المرحلة التانية → لازم
            // تعيد مسح كل حدود الخط، وتلاقي حد 1→2 (100) متغطّي خلاص
            // بالرصيد اللي الحفظة الأولى عملته فمتعملوش تاني، وتكتشف فجوة
            // جديدة منفصلة عند حد 2→3 (10 - 0 = 10)
            using (var scope2 = _db.CreateScope())
            {
                var service2 = _db.GetService<ProductionFlowService>(scope2);
                var ranges = new[]
                {
                    new FlowRangeDto { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 10 }
                };
                var shares = new[]
                {
                    new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 10 }
                };
                await service2.RecordFlowAsync(TestDatabase.ProductBagId, Day, ranges, shares, confirmOverride: true);
            }

            using var check = _db.CreateScope();
            var appDb = _db.GetService<AppDbContext>(check);
            var balances = await appDb.InitialBalances
                .Include(b => b.Ranges)
                .Where(b => b.ProductId == TestDatabase.ProductBagId)
                .ToListAsync();

            // رصيدين بس — مفيش تكرار للـ 100 اللي اتغطّت خلاص، وفجوة حد
            // 2→3 (10 قطع) اتضافت كرصيد جديد منفصل
            Assert.Equal(2, balances.Count);
            Assert.Contains(balances, b => b.Quantity == 100 && b.Ranges.Single().FromStageId == TestDatabase.BagStage2Id);
            Assert.Contains(balances, b => b.Quantity == 10 && b.Ranges.Single().FromStageId == TestDatabase.BagStage3Id);
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
