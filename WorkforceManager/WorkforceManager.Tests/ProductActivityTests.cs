using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// نشاط المنتجات — الأساس اللي شاشة المنتجات بتفلتر وتحسب منه.
    ///
    /// الفكرة اللي الاختبارات دي بتحرسها: **"شغّال" معناها اشتغل فعلًا**،
    /// مش إن الفلاج <c>IsActive</c> مفعّل. منتج متسيب من شهور كان بيفضل
    /// "نشط" على طول، فالرقم مكانش بيقول حاجة.
    /// </summary>
    public class ProductActivityTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        /// <summary>
        /// <c>confirmOverride: true</c> عشان قاعدة التكليف (عامل واحد على
        /// مرحلة واحدة في اليوم) متوقفش اختبارات مالهاش علاقة بيها —
        /// القاعدة نفسها متغطية في اختباراتها.
        /// </summary>
        private async Task RecordAsync(int stageId, int pieces, DateTime date, int workerId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private async Task<IReadOnlyList<Business.DTOs.ProductActivityDto>> ActivityAsync(
            DateTime from, DateTime to)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<ProductActivityService>(scope).GetAsync(from, to);
        }

        private static Business.DTOs.ProductActivityDto Bag(
            IReadOnlyList<Business.DTOs.ProductActivityDto> all) =>
            all.Single(p => p.ProductId == TestDatabase.ProductBagId);

        // ======================= "شغّال" = اشتغل فعلًا =======================

        [Fact]
        public async Task A_product_with_no_records_is_not_working_even_though_it_is_active()
        {
            var activity = await ActivityAsync(Today.AddDays(-7), Today);

            var bag = Bag(activity);
            Assert.True(bag.IsActive);          // الفلاج مفعّل
            Assert.False(bag.WorkedInPeriod);   // بس مفيش شغل
            Assert.Equal(0, bag.PiecesProduced);
        }

        [Fact]
        public async Task Recorded_production_makes_the_product_working()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today, TestDatabase.WorkerAhmedId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.True(bag.WorkedInPeriod);
            Assert.Equal(100, bag.PiecesProduced);
            Assert.Equal(1, bag.DaysWorked);
        }

        [Fact]
        public async Task Pieces_add_up_across_stages_and_days()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 60, Today, TestDatabase.WorkerSaidId);
            await RecordAsync(TestDatabase.BagStage1Id, 40, Today.AddDays(-1), TestDatabase.WorkerAhmedId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.Equal(200, bag.PiecesProduced);
            Assert.Equal(2, bag.DaysWorked);
        }

        // ======================= الفترة بتحكم كل حاجة =======================

        [Fact]
        public async Task Work_outside_the_period_does_not_count()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today.AddDays(-40), TestDatabase.WorkerAhmedId);

            var insidePeriod = Bag(await ActivityAsync(Today.AddDays(-7), Today));
            Assert.False(insidePeriod.WorkedInPeriod);

            // نفس المنتج بيبقى شغّال لو وسّعنا الفترة
            var widerPeriod = Bag(await ActivityAsync(Today.AddDays(-60), Today));
            Assert.True(widerPeriod.WorkedInPeriod);
            Assert.Equal(100, widerPeriod.PiecesProduced);
        }

        [Fact]
        public void Default_period_is_the_same_work_week_as_the_rest_of_the_app()
        {
            // تعريف تاني للأسبوع في شاشة واحدة كان هيدي أرقام مختلفة عن
            // كشف الأجور لنفس الفترة
            var fromActivity = ProductActivityService.CurrentWeek(Today);
            var fromWeekly = WeeklySummaryService.GetWorkWeekRange(Today);

            Assert.Equal(fromWeekly.WeekStart, fromActivity.From);
            Assert.Equal(fromWeekly.WeekEnd, fromActivity.To);
            Assert.Equal(DayOfWeek.Thursday, fromActivity.From.DayOfWeek);
        }

        // ======================= بيانات الفلاتر =======================

        [Fact]
        public async Task Worker_ids_list_who_actually_worked_on_the_product()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today, TestDatabase.WorkerAhmedId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.Contains(TestDatabase.WorkerAhmedId, bag.WorkerIds);
            Assert.DoesNotContain(TestDatabase.WorkerSaidId, bag.WorkerIds);
        }

        [Fact]
        public async Task Stage_ids_cover_the_products_active_line()
        {
            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.Contains(TestDatabase.BagStage1Id, bag.StageIds);
            Assert.Contains(TestDatabase.BagStage2Id, bag.StageIds);
            Assert.Contains(TestDatabase.BagStage3Id, bag.StageIds);
        }

        // ======================= الترتيب للإحصائيات =======================

        [Fact]
        public async Task Products_come_back_sorted_by_volume_for_the_top_and_bottom_stats()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 500, Today, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.RingStage1Id, 50, Today, TestDatabase.WorkerAhmedId);

            var activity = await ActivityAsync(Today.AddDays(-7), Today);
            var worked = activity.Where(p => p.WorkedInPeriod).ToList();

            Assert.Equal(TestDatabase.ProductBagId, worked.First().ProductId);
            Assert.Equal(TestDatabase.ProductRingId, worked.Last().ProductId);
        }
    }
}
