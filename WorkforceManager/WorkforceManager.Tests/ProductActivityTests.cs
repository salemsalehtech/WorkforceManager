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
            Assert.Equal(0, bag.CompletedPieces);
        }

        [Fact]
        public async Task Work_that_stops_before_the_last_stage_is_not_completed_output()
        {
            // القطع دخلت الخط بس ماوصلتش لآخره — شغل موجود، إنتاج تام لأ
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today, TestDatabase.WorkerAhmedId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.True(bag.WorkedInPeriod);
            Assert.Equal(0, bag.CompletedPieces);
            Assert.Equal(100, bag.StartedPieces);
            Assert.Equal(1, bag.DaysWorked);
        }

        [Fact]
        public async Task A_piece_that_crossed_every_stage_counts_once_not_once_per_stage()
        {
            // **العطل اللي كان موجود**: 100 قطعة عدّت 3 مراحل كانت
            // بتتحسب 300. القطعة بتعدّي المراحل بالترتيب مش بالتوازي،
            // فمجموع المراحل مش بيقيس إنتاج.
            await RecordAsync(TestDatabase.BagStage1Id, 100, Today, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 100, Today, TestDatabase.WorkerSaidId);
            await RecordAsync(TestDatabase.BagStage3Id, 100, Today, TestDatabase.WorkerMonaHourlyId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.Equal(100, bag.CompletedPieces);   // مش 300
            Assert.Equal(100, bag.StartedPieces);

            // مجموع المراحل لسه محفوظ — بس كمقياس شغل مش إنتاج
            Assert.Equal(300, bag.StageWorkPieces);
        }

        [Fact]
        public async Task Completed_pieces_add_up_across_days()
        {
            await RecordAsync(TestDatabase.BagStage3Id, 100, Today, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage3Id, 40, Today.AddDays(-1), TestDatabase.WorkerSaidId);

            var bag = Bag(await ActivityAsync(Today.AddDays(-7), Today));

            Assert.Equal(140, bag.CompletedPieces);
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
            Assert.Equal(100, widerPeriod.StartedPieces);
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
        public async Task Products_are_sorted_by_completed_output_not_by_stage_work()
        {
            // الشنطة 3 مراحل والدبلة مرحلتين. لو الترتيب بمجموع المراحل،
            // الشنطة (100×3 = 300) هتسبق الدبلة (200×2 = 400)... غلط.
            // الترتيب بالتام: الدبلة 200 والشنطة 100، فالدبلة الأولى.
            await RecordAsync(TestDatabase.BagStage3Id, 100, Today, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.RingStage2Id, 200, Today, TestDatabase.WorkerSaidId);

            var activity = await ActivityAsync(Today.AddDays(-7), Today);
            var worked = activity.Where(p => p.WorkedInPeriod).ToList();

            Assert.Equal(TestDatabase.ProductRingId, worked.First().ProductId);
            Assert.Equal(TestDatabase.ProductBagId, worked.Last().ProductId);
        }
    }
}
