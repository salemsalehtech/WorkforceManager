using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// رسم إنتاج المنتجات: نفس الأرقام مقسومة بيوم أو أسبوع أو شهر.
    ///
    /// الخطر الحقيقي هنا مش إن الرسم يقع — هو إن يقول رقم غير اللي
    /// شاشة إنتاج اليوم بتقوله عن نفس اليوم. القطعة بتعدي على 11 مرحلة،
    /// فأي جمع غلط بيضرب الرقم في 11 والرسم بيفضل شكله سليم.
    /// </summary>
    public class ProductionChartTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        private async Task ProduceAsync(int stageId, int pieces, DateTime day)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, stageId, pieces, day, confirmOverride: true);
        }

        private Task<List<Business.DTOs.ProductOutputPointDto>> ChartAsync(
            DateTime from, DateTime to, ChartGrain grain) =>
            _db.InScopeAsync<ProductionChartService, List<Business.DTOs.ProductOutputPointDto>>(
                s => s.GetProductOutputAsync(from, to, grain));

        // ---------------- التقسيم ----------------

        [Fact]
        public async Task ByDay_EachDayIsItsOwnColumn()
        {
            await ProduceAsync(TestDatabase.BagStage3Id, 100, Today.AddDays(-2));
            await ProduceAsync(TestDatabase.BagStage3Id, 200, Today.AddDays(-1));

            var points = await ChartAsync(Today.AddDays(-2), Today, ChartGrain.Day);

            Assert.Equal(2, points.Count);
            Assert.Equal(new[] { 100, 200 }, points.Select(p => p.CompletedPieces));
        }

        [Fact]
        public async Task ByWeek_TheSameDaysCollapseIntoOneColumn()
        {
            // نفس اليومين بالظبط: بالأسبوع بيبقوا عمود واحد مجموعه
            // الاتنين — لو طلعوا عمودين يبقى تعريف الأسبوع اتكسر
            await ProduceAsync(TestDatabase.BagStage3Id, 100, Today.AddDays(-2));
            await ProduceAsync(TestDatabase.BagStage3Id, 200, Today.AddDays(-1));

            var points = await ChartAsync(Today.AddDays(-2), Today, ChartGrain.Week);

            // الأسبوع خميس→أربع، فاليومين ممكن يقعوا في أسبوعين
            Assert.Equal(300, points.Sum(p => p.CompletedPieces));
            Assert.True(points.Count <= 2);
        }

        [Fact]
        public async Task ByMonth_EverythingInOneMonthIsOneColumn()
        {
            // يومين مختلفين في نفس الشهر — نفس العامل على نفس المرحلة
            // في نفس اليوم تكرار محظور أصلاً
            await ProduceAsync(TestDatabase.BagStage3Id, 100, Today.AddDays(-1));
            await ProduceAsync(TestDatabase.BagStage3Id, 250, Today);

            var points = await ChartAsync(Today.AddDays(-1), Today, ChartGrain.Month);

            Assert.Single(points);
            Assert.Equal(350, points[0].CompletedPieces);
            Assert.Equal(new DateTime(Today.Year, Today.Month, 1), points[0].BucketStart);
        }

        [Fact]
        public void TheWeekBucketIsTheWorkWeek_NotTheCalendarWeek()
        {
            // الأسبوع هنا لازم يبقى نفس أسبوع الأجور (خميس → أربع)،
            // وإلا الرسم والكشف بيتكلموا عن أسبوعين مختلفين
            var bucket = ProductionChartService.BucketOf(Today, ChartGrain.Week);
            var payrollWeek = WeeklySummaryService.GetWorkWeekRange(Today);

            Assert.Equal(payrollWeek.WeekStart, bucket.Start);
            Assert.Equal(payrollWeek.WeekEnd, bucket.End);
        }

        // ---------------- الأرقام ----------------

        [Fact]
        public async Task OnlyTheLastStageCounts_NotEveryStage()
        {
            // القطعة بتعدي على كل المراحل، فجمعها كلها بيعد نفس القطعة
            // مرة لكل مرحلة
            await ProduceAsync(TestDatabase.BagStage1Id, 500, Today);
            await ProduceAsync(TestDatabase.BagStage3Id, 500, Today);

            var points = await ChartAsync(Today, Today, ChartGrain.Day);

            Assert.Equal(500, points.Sum(p => p.CompletedPieces));
        }

        [Fact]
        public async Task ScrapOnTheLastStage_IsSubtractedFromCompleted()
        {
            await ProduceAsync(TestDatabase.BagStage3Id, 1000, Today);

            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(TestDatabase.BagStage3Id, Today, 100, "", note: "رفض جودة");

            var points = await ChartAsync(Today, Today, ChartGrain.Day);

            Assert.Equal(900, points.Sum(p => p.CompletedPieces));
        }

        [Fact]
        public async Task ScrapOnAnEarlyStage_IsShownButNotSubtractedTwice()
        {
            // الهالك المبكر مش داخل أصلاً في رقم آخر مرحلة (القطعة
            // مكملتش لحد هناك)، فخصمه كمان كان هيعده مرتين. بيتعرض
            // كهالك عشان السؤال "طلعلي كام هالك؟" ليه إجابة.
            await ProduceAsync(TestDatabase.BagStage3Id, 1000, Today);

            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(TestDatabase.BagStage1Id, Today, 300, "", note: "عيب خامة");

            var points = await ChartAsync(Today, Today, ChartGrain.Day);

            Assert.Equal(1000, points.Sum(p => p.CompletedPieces));
            Assert.Equal(300, points.Sum(p => p.ScrapPieces));
        }

        [Fact]
        public async Task TheChartSaysTheSameCompletedNumberAsTheDailyScreen()
        {
            // الرقمين بيتشافوا في نفس الشاشة على تبويبين — أي فرق
            // بينهم بيتحوّل لسؤال محدش يقدر يجاوبه
            await ProduceAsync(TestDatabase.BagStage1Id, 800, Today);
            await ProduceAsync(TestDatabase.BagStage3Id, 600, Today);

            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(TestDatabase.BagStage3Id, Today, 50, "", note: "رفض جودة");

            var daily = await _db.InScopeAsync<DailyProductionReportService, Business.DTOs.DailyProductionReportDto>(
                s => s.GetAsync(Today));

            var points = await ChartAsync(Today, Today, ChartGrain.Day);

            Assert.Equal(daily.TotalCompletedPieces, points.Sum(p => p.CompletedPieces));
        }

        [Fact]
        public async Task AProductWhoseWholeDayWasScrapped_StillAppears()
        {
            // من غير كده المنتج بيختفي من الرسم خالص، والمستخدم يفتكر
            // إن محدش اشتغل عليه — وهو اشتغل وضاع
            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(TestDatabase.BagStage3Id, Today, 400, "", note: "رفض جودة");

            var points = await ChartAsync(Today, Today, ChartGrain.Day);

            var point = Assert.Single(points);
            Assert.Equal(0, point.CompletedPieces);
            Assert.Equal(400, point.ScrapPieces);
            Assert.NotEqual("—", point.ProductName);
        }

        [Fact]
        public async Task CompletedPieces_ReadsTheActualOutputNumber_NotTheWorkersSum()
        {
            // العامل عمل 130 ضربة على آخر مرحلة، بس الإنتاج الفعلي
            // المسجَّل للنطاق 100 — رقمين منفصلين تمامًا.
            var range = new FlowRangeDto
            {
                FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 100
            };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 130 }
            };

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, Today, new[] { range }, shares, confirmOverride: true);

            var point = Assert.Single(await ChartAsync(Today, Today, ChartGrain.Day));
            Assert.Equal(100, point.CompletedPieces);
        }

        // ---------------- محور الزمن ----------------

        [Fact]
        public void StartOfLast_CountsBucketsBackwards_NotDays()
        {
            // بيتقاس من اليوم الحقيقي مش من يوم الاختبار الثابت:
            // "آخر 3 شهور" معناها من دلوقتي، مش من تاريخ في البذرة.
            // و**3 شهور مش 90 يوم** — الطرح بالأيام بيوقع في نص شهر.
            var threeMonths = ProductionChartService.StartOfLast(3, ChartGrain.Month);
            var expected = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);

            Assert.Equal(expected, threeMonths);

            var sevenDays = ProductionChartService.StartOfLast(7, ChartGrain.Day);
            Assert.Equal(DateTime.Today.AddDays(-6), sevenDays);
        }

        [Fact]
        public void NextBucket_MovesOneWholePeriod()
        {
            var month = new DateTime(2026, 1, 31);
            Assert.Equal(new DateTime(2026, 2, 28), ProductionChartService.NextBucket(month, ChartGrain.Month));

            var day = new DateTime(2026, 8, 10);
            Assert.Equal(new DateTime(2026, 8, 11), ProductionChartService.NextBucket(day, ChartGrain.Day));
            Assert.Equal(new DateTime(2026, 8, 17), ProductionChartService.NextBucket(day, ChartGrain.Week));
        }
    }
}
