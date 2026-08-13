using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات <see cref="ProductionStageOutputService"/> — الإنتاج الفعلي
    /// المستقل عن قطع العمال.
    ///
    /// الفكرة اللي الاختبارات دي بتحرسها: **تراكمي، مش استبدال** (تسجيل
    /// تاني على نفس المرحلة/اليوم يتجمّع، ما يمسحش الأول — نفس اللي
    /// بيحصل مع ProductionScrap ومجموع DailyProduction)، و**رجوع تلقائي
    /// لمجموع قطع العمال القديم** لأي مرحلة/يوم مالهاش سجل في الجدول
    /// الجديد — من غيره كل تقرير على بيانات قبل هذا الفيتشر كان يطلع صفر.
    ///
    /// RecordOutputAsync عن قصد مبيناديش SaveChangesAsync (عشان يشارك
    /// معاملة ProductionFlowService في الاستخدام الحقيقي)، فكل اختبار
    /// هنا بينادي عليه بنفسه بعد النداء مباشرة.
    /// </summary>
    public class ProductionStageOutputTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => TestDatabase.Today.AddDays(1);

        private async Task RecordAsync(int stageId, DateTime date, int pieces)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<ProductionStageOutputService>(scope).RecordOutputAsync(stageId, date, pieces);
            await _db.GetService<AppDbContext>(scope).SaveChangesAsync();
        }

        [Fact]
        public async Task RecordingTwice_OnTheSameStageAndDay_Accumulates()
        {
            await RecordAsync(TestDatabase.RingStage1Id, Day1, 100);
            await RecordAsync(TestDatabase.RingStage1Id, Day1, 50);

            var totals = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(Day1));

            Assert.Equal(150, totals[TestDatabase.RingStage1Id]);
        }

        [Fact]
        public async Task RecordingOnDifferentDays_KeepsThemSeparate()
        {
            await RecordAsync(TestDatabase.RingStage1Id, Day1, 100);
            await RecordAsync(TestDatabase.RingStage1Id, Day2, 40);

            var day1Totals = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(Day1));
            var day2Totals = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(Day2));

            Assert.Equal(100, day1Totals[TestDatabase.RingStage1Id]);
            Assert.Equal(40, day2Totals[TestDatabase.RingStage1Id]);
        }

        [Fact]
        public async Task StoredRow_MatchesWhatWasRecorded()
        {
            await RecordAsync(TestDatabase.RingStage1Id, Day1, 100);
            await RecordAsync(TestDatabase.RingStage1Id, Day1, 50);

            var row = Assert.Single(await _db.GetProductionStageOutputsAsync());
            Assert.Equal(TestDatabase.RingStage1Id, row.ProductionStageId);
            Assert.Equal(Day1.Date, row.Date);
            Assert.Equal(150, row.PieceCount);
        }

        // ======================= الرجوع لمجموع العمال القديم =======================

        [Fact]
        public async Task WithNoNewTableRow_FallsBackToTheWorkersLegacySum()
        {
            // بيانات "قبل الفيتشر": سجلات عمال من غير أي سجل في الجدول الجديد
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.DailyProductions.Add(new Core.Models.DailyProduction
                {
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Date = Day1,
                    PieceCount = 70,
                    PiecesPerWorkdayAtEntry = 10
                });
                await db.SaveChangesAsync();
            }

            var totals = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(Day1));

            Assert.Equal(70, totals[TestDatabase.RingStage1Id]);
        }

        [Fact]
        public async Task WithANewTableRow_TheNewNumberWinsOverTheWorkersSum_EvenIfDifferent()
        {
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.DailyProductions.Add(new Core.Models.DailyProduction
                {
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Date = Day1,
                    PieceCount = 999, // عدد ضربات العامل — مختلف عن الإنتاج الفعلي عمدًا
                    PiecesPerWorkdayAtEntry = 10
                });
                await db.SaveChangesAsync();
            }

            await RecordAsync(TestDatabase.RingStage1Id, Day1, 40);

            var totals = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsOnAsync(Day1));

            Assert.Equal(40, totals[TestDatabase.RingStage1Id]);
        }

        [Fact]
        public async Task CumulativeTotal_MixesNewAndLegacyDays_WithoutDoubleCounting()
        {
            // يوم قديم (قبل الفيتشر): عمال بس
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.DailyProductions.Add(new Core.Models.DailyProduction
                {
                    WorkerId = TestDatabase.WorkerAhmedId,
                    ProductionStageId = TestDatabase.RingStage1Id,
                    Date = Day1,
                    PieceCount = 70,
                    PiecesPerWorkdayAtEntry = 10
                });
                await db.SaveChangesAsync();
            }

            // يوم جديد (بعد الفيتشر): رقم إنتاج فعلي مستقل
            await RecordAsync(TestDatabase.RingStage1Id, Day2, 40);

            var cumulative = await _db.InScopeAsync<ProductionStageOutputService, IReadOnlyDictionary<int, int>>(
                svc => svc.GetStageTotalsUpToAsync(Day2));

            // 70 من اليوم القديم (رجوع تلقائي) + 40 من اليوم الجديد (الجدول
            // الجديد) = 110. لو اليوم الجديد اتحسب مرتين (مرة من الجدول
            // الجديد ومرة من مجموع العمال) الناتج كان يطلع 150 غلط
            Assert.Equal(110, cumulative[TestDatabase.RingStage1Id]);
        }

        [Fact]
        public async Task HasAnyForStage_IsFalse_UntilSomethingIsRecorded()
        {
            Assert.False(await _db.InScopeAsync<ProductionStageOutputService, bool>(
                svc => svc.HasAnyForStageAsync(TestDatabase.RingStage1Id)));

            await RecordAsync(TestDatabase.RingStage1Id, Day1, 10);

            Assert.True(await _db.InScopeAsync<ProductionStageOutputService, bool>(
                svc => svc.HasAnyForStageAsync(TestDatabase.RingStage1Id)));
        }
    }
}
