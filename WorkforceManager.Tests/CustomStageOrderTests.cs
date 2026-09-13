using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الترتيب المخصص لجلسة واحدة — **حدوده**، مش وظيفته.
    ///
    /// القاعدة اللي الملف ده بيحرسها: الترتيب المخصص بيغيّر التحقق من
    /// النطاقات في الجلسة اللي اتبعت فيها، **وبس**. لا بيلمس حساب فجوات
    /// الخط، ولا التقارير، ولا الجلسة اللي بعده.
    ///
    /// شنطة = قص(4) → خياطة(5) → تشطيب(6) في الخط الحقيقي.
    /// </summary>
    public class CustomStageOrderTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        private const int Cut = TestDatabase.BagStage1Id;      // قص — الأولى
        private const int Sew = TestDatabase.BagStage2Id;      // خياطة — التانية
        private const int Finish = TestDatabase.BagStage3Id;   // تشطيب — التالتة

        private static FlowRangeDto Range(int from, int to, int pieces) =>
            new() { FromStageId = from, ToStageId = to, PieceCount = pieces };

        private static FlowShareDto Share(int stageId, int pieces) =>
            new() { ProductionStageId = stageId, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = pieces };

        private async Task<FlowSaveResultDto> RecordAsync(
            IReadOnlyList<FlowRangeDto> ranges, IReadOnlyList<FlowShareDto> shares,
            IReadOnlyList<int>? customOrder = null, DateTime? date = null)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, date ?? Today, ranges, shares,
                confirmOverride: true, customStageOrder: customOrder);
        }

        // ======================= الغرض: الترتيب بيغيّر التحقق =======================

        [Fact]
        public async Task A_range_that_runs_backwards_on_the_real_line_is_refused_normally()
        {
            // تشطيب → قص معكوس في الخط الحقيقي
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(
                    new[] { Range(Finish, Cut, 100) },
                    new[] { Share(Cut, 100), Share(Sew, 100), Share(Finish, 100) }));

            Assert.Contains("معكوس", ex.Message);
        }

        [Fact]
        public async Task The_same_range_is_accepted_when_the_plan_puts_those_stages_in_that_order()
        {
            // نفس النطاق بالظبط، بس الجلسة جاية من خطة ترتيبها
            // تشطيب → خياطة → قص. دي الميزة نفسها.
            var result = await RecordAsync(
                new[] { Range(Finish, Cut, 100) },
                new[] { Share(Cut, 100), Share(Sew, 100), Share(Finish, 100) },
                customOrder: new[] { Finish, Sew, Cut });

            Assert.True(result.RecordsCount > 0);

            // التلات مراحل اتسجّل عليها إنتاج فعلاً
            var stageIds = (await _db.GetProductionAsync()).Select(p => p.ProductionStageId).Distinct().ToList();
            Assert.Equal(3, stageIds.Count);
        }

        [Fact]
        public async Task A_plan_that_leaves_a_stage_out_records_only_the_stages_it_planned()
        {
            // خطة: قص → تشطيب (من غير خياطة)
            await RecordAsync(
                new[] { Range(Cut, Finish, 80) },
                new[] { Share(Cut, 80), Share(Finish, 80) },
                customOrder: new[] { Cut, Finish });

            var stageIds = (await _db.GetProductionAsync())
                .Select(p => p.ProductionStageId).Distinct().OrderBy(x => x).ToList();

            Assert.Equal(new[] { Cut, Finish }, stageIds);
        }

        [Fact]
        public async Task A_worker_put_on_a_stage_the_plan_left_out_is_refused()
        {
            // المرحلة مش في خط الجلسة دي خالص، فنصيب عليها مالوش معنى
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(
                    new[] { Range(Cut, Finish, 80) },
                    new[] { Share(Cut, 80), Share(Sew, 80), Share(Finish, 80) },
                    customOrder: new[] { Cut, Finish }));
        }

        [Fact]
        public async Task A_plan_still_cannot_cover_the_same_stage_twice()
        {
            // منع التسجيل المزدوج قاعدة مطلقة — الترتيب المخصص مش بيلغيها
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(
                    new[] { Range(Finish, Sew, 50), Range(Sew, Cut, 50) },
                    new[] { Share(Cut, 50), Share(Sew, 50), Share(Finish, 50) },
                    customOrder: new[] { Finish, Sew, Cut }));

            Assert.Contains("مرة واحدة", ex.Message);
        }

        // ======================= الحدود: ميوصلش لحتة تانية =======================

        [Fact]
        public async Task The_custom_order_never_leaks_into_the_next_session()
        {
            // جلسة خطة بترتيب مقلوب النهارده...
            await RecordAsync(
                new[] { Range(Finish, Cut, 100) },
                new[] { Share(Cut, 100), Share(Sew, 100), Share(Finish, 100) },
                customOrder: new[] { Finish, Sew, Cut });

            // ...وبكرة جلسة عادية لنفس المنتج بنفس النطاق المعكوس:
            // لازم ترفض زي ما كانت هترفض من الأول
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(
                    new[] { Range(Finish, Cut, 100) },
                    new[] { Share(Cut, 100), Share(Sew, 100), Share(Finish, 100) },
                    date: Today.AddDays(1)));

            Assert.Contains("معكوس", ex.Message);
        }

        [Fact]
        public async Task Gap_balances_are_computed_on_the_real_line_not_the_custom_one()
        {
            // الخطة بتتخطى الخياطة: قص 100، تشطيب 100.
            // على الخط الحقيقي ده معناه إن 100 قطعة عدّت الخياطة من غير
            // ما تتسجّل عليها — فجوة حقيقية عند الحد الفاصل قص→خياطة،
            // ولازم تتحول رصيد أولي. لو الحساب كان ماشي على الترتيب
            // المخصص (قص→تشطيب) مكانش هيشوف الفجوة دي أصلاً.
            await RecordAsync(
                new[] { Range(Cut, Cut, 100), Range(Finish, Finish, 100) },
                new[] { Share(Cut, 100), Share(Finish, 100) },
                customOrder: new[] { Cut, Finish });

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);

            var balances = await db.InitialBalances
                .Include(b => b.Ranges)
                .Where(b => b.ProductId == TestDatabase.ProductBagId)
                .ToListAsync();

            // الرصيد المتولّد بيبدأ من الخياطة — المرحلة اللي الخط
            // الحقيقي بيقول إن الشغل واقف عندها، مش اللي الخطة شايفاها
            Assert.Contains(balances.SelectMany(b => b.Ranges), r => r.FromStageId == Sew);
        }

        [Fact]
        public async Task Reports_read_the_products_real_line_after_a_custom_order_session()
        {
            // خطة مقلوبة: تشطيب → خياطة → قص
            await RecordAsync(
                new[] { Range(Finish, Cut, 100) },
                new[] { Share(Cut, 100), Share(Sew, 100), Share(Finish, 100) },
                customOrder: new[] { Finish, Sew, Cut });

            using var scope = _db.CreateScope();
            var report = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Today);

            var bag = Assert.Single(report.Products, p => p.ProductId == TestDatabase.ProductBagId);

            // "التام" لسه = إنتاج آخر مرحلة في الخط **الحقيقي** (تشطيب)،
            // مش آخر مرحلة في ترتيب الخطة (قص). الرقم ده اللي كل
            // التقارير بتعرضه كإنتاج المصنع
            Assert.Equal(100, bag.CompletedPieces);
            Assert.Equal(100, bag.StartedPieces);
        }

        [Fact]
        public async Task A_normal_session_is_completely_unaffected_by_an_existing_plan()
        {
            // خطة محفوظة بترتيب مقلوب موجودة في القاعدة...
            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionMemoryService>(scope).CreateAsync(
                    TestDatabase.ProductBagId, new[] { Finish, Sew, Cut }, "", Today);

            // ...والجلسة العادية عمرها ما بتشوفها: الترتيب الحقيقي شغال زي ما هو
            var result = await RecordAsync(
                new[] { Range(Cut, Finish, 60) },
                new[] { Share(Cut, 60), Share(Sew, 60), Share(Finish, 60) });

            Assert.True(result.RecordsCount > 0);
        }
    }
}
