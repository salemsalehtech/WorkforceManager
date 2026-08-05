using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات إنتاج اليوم — الرقمين اللي المصنع بيشتغل عليهم:
    /// **خلص كام** و**دخل الخط كام** في اليوم ده.
    ///
    /// الفكرة اللي الاختبارات دي بتحرسها: **مفيش جدول بيتتبّع القطع**.
    /// التام = إنتاج آخر مرحلة في اليوم، والداخل = إنتاج أول مرحلة في اليوم.
    /// الاتنين محسوبين من سجلات الإنتاج نفسها، من غير ما المستخدم يجاوب على
    /// أي سؤال عن "القطع دي جاية منين".
    ///
    /// خط الشنطة: 1.قص → 2.خياطة → 3.تشطيب (اليومية 10 لكل مرحلة).
    /// </summary>
    public class ProductionOutputTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => TestDatabase.Today.AddDays(1);

        private static readonly int[] BagLine =
        {
            TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id
        };

        /// <summary>
        /// يسجل نطاق واحد بعامل واحد على كل مرحلة فيه.
        ///
        /// ملحوظة على اختيار العامل: قاعدة التكليف بتمنع نفس العامل على نفس
        /// المرحلة مرتين في نفس اليوم، فاختبار بيسجل على نفس المرحلة مرتين
        /// في يوم لازم يبعت عامل مختلف.
        /// </summary>
        private async Task RecordAsync(
            DateTime date, int fromStageId, int toStageId, int pieces,
            int workerId = TestDatabase.WorkerAhmedId)
        {
            var fromIndex = Array.IndexOf(BagLine, fromStageId);
            var toIndex = Array.IndexOf(BagLine, toStageId);

            var shares = new List<FlowShareDto>();
            for (var i = fromIndex; i <= toIndex; i++)
                shares.Add(new FlowShareDto
                {
                    ProductionStageId = BagLine[i],
                    WorkerId = workerId,
                    PieceCount = pieces
                });

            var range = new FlowRangeDto
            {
                FromStageId = fromStageId,
                ToStageId = toStageId,
                PieceCount = pieces
            };

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, date, new[] { range }, shares, confirmOverride: true);
        }

        private async Task<DailyProductionReportDto> ReportAsync(DateTime date)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<DailyProductionReportService>(scope).GetAsync(date);
        }

        private static DailyProductReportDto Bag(DailyProductionReportDto report) =>
            Assert.Single(report.Products);

        // ======================= التام =======================

        [Fact]
        public async Task Completed_output_is_what_passed_the_last_stage()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100);

            var report = await ReportAsync(Day1);

            Assert.Equal(100, report.TotalCompletedPieces);
            Assert.Equal(100, report.TotalStartedPieces);
        }

        [Fact]
        public async Task Work_that_stops_before_the_last_stage_is_not_completed_output()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100);

            var report = await ReportAsync(Day1);

            Assert.Equal(0, report.TotalCompletedPieces);
            Assert.Equal(100, report.TotalStartedPieces);
        }

        [Fact]
        public async Task Work_that_starts_mid_line_counts_as_completed_but_not_as_started()
        {
            // شغل بدأ من نص الخط: خلّص المنتج بس مدخّلش قطع جديدة على الخط
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100);

            var bag = Bag(await ReportAsync(Day1));

            Assert.Equal(100, bag.CompletedPieces);
            Assert.Equal(0, bag.StartedPieces);
        }

        // ======================= التقرير لليوم بتاعه =======================

        [Fact]
        public async Task Old_day_report_does_not_change_after_later_work()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100,
                TestDatabase.WorkerSaidId);

            // تقرير يوم 1 بيتحسب من سجلات يوم 1 بس — شغل يوم 2 عمره ما
            // يغيّره بأثر رجعي
            var day1 = Bag(await ReportAsync(Day1));
            Assert.Equal(0, day1.CompletedPieces);
            Assert.Equal(100, day1.StartedPieces);

            // وشغل يوم 2 بيبان في يوم 2 لوحده
            var day2 = Bag(await ReportAsync(Day2));
            Assert.Equal(100, day2.CompletedPieces);
            Assert.Equal(0, day2.StartedPieces);
        }

        [Fact]
        public async Task A_day_with_no_records_reports_nothing()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100);

            var day2 = await ReportAsync(Day2);

            Assert.Empty(day2.Products);
            Assert.Equal(0, day2.TotalCompletedPieces);
            Assert.Equal(0, day2.TotalStartedPieces);
        }

        // ======================= الأجور مستقلة تمامًا =======================

        [Fact]
        public async Task Wages_are_untouched_by_where_the_pieces_stopped()
        {
            // العامل بياخد أجره على اللي عمله، مهما كانت القطع وقفت فين
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100);

            var records = await _db.GetProductionAsync();

            // مرحلتين × 100 قطعة ÷ يومية 10 = 20 يومية
            Assert.Equal(20m, records.Sum(r => r.WorkdaysCompleted));
            Assert.All(records, r => Assert.Equal(Day1.Date, r.Date.Date));
        }

        // ======================= الإقفال =======================

        [Fact]
        public async Task Closure_preview_shows_the_days_numbers()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 300);
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100,
                TestDatabase.WorkerSaidId);

            using var scope = _db.CreateScope();
            var preview = await _db.GetService<DayClosureService>(scope).PreviewAsync(Day1);

            Assert.Equal(100, preview.CompletedPieces);
            Assert.Equal(300, preview.StartedPieces);
            Assert.False(preview.AlreadyClosed);

            var product = Assert.Single(preview.ByProduct);
            Assert.Equal("شنطة", product.ProductName);
            Assert.Equal(100, product.CompletedPieces);
            Assert.Equal(300, product.StartedPieces);
        }

        [Fact]
        public async Task Closing_a_day_blocks_further_production_on_it()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50,
                    TestDatabase.WorkerSaidId));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task Closed_day_does_not_block_the_next_day()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            // الشغل بيكمّل بكرة عادي — الإقفال بيثبّت أرقام يوم مش بيوقف الخط
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100,
                TestDatabase.WorkerSaidId);

            Assert.Equal(100, (await ReportAsync(Day2)).TotalCompletedPieces);
        }

        [Fact]
        public async Task Reopening_a_day_allows_recording_again()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);

            using (var scope = _db.CreateScope())
            {
                var closure = _db.GetService<DayClosureService>(scope);
                await closure.CloseAsync(Day1);
                await closure.ReopenAsync(Day1);
            }

            // غلط الإدخال وارد — حبس المستخدم بره يومه مش حل
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50,
                TestDatabase.WorkerSaidId);

            Assert.Equal(150, Bag(await ReportAsync(Day1)).StartedPieces);
        }

        // ======================= التصحيح المباشر =======================

        [Fact]
        public async Task Correcting_a_record_updates_the_numbers_that_depend_on_it()
        {
            // الأرقام محسوبة من السجلات، فتصحيح سجل بيصحّح التقرير لوحده —
            // مفيش دفعة لازم تتصحّح معاه بالإيد
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            var record = Assert.Single(await _db.GetProductionAsync());

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .UpdateProductionAsync(record.Id, 60);

            Assert.Equal(60, Bag(await ReportAsync(Day1)).StartedPieces);
        }
    }
}
