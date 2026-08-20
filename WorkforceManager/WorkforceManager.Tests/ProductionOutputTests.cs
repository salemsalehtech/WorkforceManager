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

        private async Task<DailyProductionReportDto> ReportForRangeAsync(DateTime from, DateTime to)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<DailyProductionReportService>(scope).GetForRangeAsync(from, to);
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

        // ======================= التقرير لمدى (أسبوع/شهر) =======================

        [Fact]
        public async Task RangeReport_SumsEachDaysNumbers_AcrossTheWholeRange()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100);
            await RecordAsync(Day2, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 60,
                TestDatabase.WorkerSaidId);

            var range = Bag(await ReportForRangeAsync(Day1, Day2));

            Assert.Equal(160, range.CompletedPieces); // 100 + 60
            Assert.Equal(160, range.StartedPieces);
        }

        [Fact]
        public async Task RangeReport_ExcludesDaysOutsideTheRange()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100);
            await RecordAsync(Day2, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 60,
                TestDatabase.WorkerSaidId);

            // مدى يوم 1 بس — يوم 2 برة النطاق
            var range = Bag(await ReportForRangeAsync(Day1, Day1));

            Assert.Equal(100, range.CompletedPieces);
        }

        [Fact]
        public async Task RangeReport_WithNoActivityAtAll_ReturnsNoProducts()
        {
            var range = await ReportForRangeAsync(Day1, Day2);

            Assert.Empty(range.Products);
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
        public async Task Correcting_a_workers_own_record_does_not_change_the_products_reported_output()
        {
            // قطعة العامل عدد ضرباته على المكنة — أساس يوميته بس. الإنتاج
            // الفعلي رقم منفصل تمامًا (رقم النطاق وقت التسجيل)، فتصحيح
            // سجل العامل بعد كده مايلمسه خالص. ده الفصل اللي الفيتشر ده
            // موجود عشانه.
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            var record = Assert.Single(await _db.GetProductionAsync());

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .UpdateProductionAsync(record.Id, 60);

            Assert.Equal(100, Bag(await ReportAsync(Day1)).StartedPieces);
        }

        // ======================= فصل قطعة العامل عن الإنتاج الفعلي =======================

        /// <summary>
        /// نطاق واحد بمجموع عمال **مختلف عمدًا** عن رقم النطاق — لمحاكاة
        /// "عدد ضربات العامل" اللي مايلزمش يساوي الإنتاج الفعلي.
        /// </summary>
        private async Task RecordWithMismatchedWorkerSumAsync(
            DateTime date, int stageId, int rangePieces, int workerPieces,
            int workerId = TestDatabase.WorkerAhmedId)
        {
            var range = new FlowRangeDto
            {
                FromStageId = stageId,
                ToStageId = stageId,
                PieceCount = rangePieces
            };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = stageId, WorkerId = workerId, PieceCount = workerPieces }
            };

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, date, new[] { range }, shares, confirmOverride: true);
        }

        [Fact]
        public async Task WorkerSumExceedingTheRangeTotal_SavesWithoutError()
        {
            // العامل عمل ضربات أكتر من الإنتاج الفعلي — جزء منها هالك أو مايكملش
            await RecordWithMismatchedWorkerSumAsync(
                Day1, TestDatabase.BagStage1Id, rangePieces: 100, workerPieces: 130);

            Assert.Equal(100, Bag(await ReportAsync(Day1)).StartedPieces);
        }

        [Fact]
        public async Task WorkerSumBelowTheRangeTotal_SavesWithoutError()
        {
            await RecordWithMismatchedWorkerSumAsync(
                Day1, TestDatabase.BagStage1Id, rangePieces: 100, workerPieces: 70);

            Assert.Equal(100, Bag(await ReportAsync(Day1)).StartedPieces);
        }

        [Fact]
        public async Task TheWorkersOwnWageBasis_IsHisOwnPieceCount_NotTheRangeTotal()
        {
            // اليومية بتتحسب من قطعة العامل نفسها، بصرف النظر عن رقم النطاق
            await RecordWithMismatchedWorkerSumAsync(
                Day1, TestDatabase.BagStage1Id, rangePieces: 100, workerPieces: 30);

            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Equal(30, record.PieceCount);
            Assert.Equal(3m, record.WorkdaysCompleted); // 30 ÷ يومية 10
        }

        [Fact]
        public async Task TheActualOutputNumber_IsStoredIndependently_MatchingTheRangeNotTheWorkerSum()
        {
            await RecordWithMismatchedWorkerSumAsync(
                Day1, TestDatabase.BagStage1Id, rangePieces: 100, workerPieces: 30);

            var row = Assert.Single(await _db.GetProductionStageOutputsAsync());
            Assert.Equal(TestDatabase.BagStage1Id, row.ProductionStageId);
            Assert.Equal(100, row.PieceCount);
        }

        [Fact]
        public async Task AStageWithPiecesButNoWorkers_StillThrows()
        {
            // القاعدة الوحيدة الباقية: لازم عامل واحد على الأقل يفضل شرط.
            // مرحلة 1 عليها عامل (عشان نتخطى شرط "وزّع عمال" العام)، ومرحلة
            // 2 عليها إنتاج بس من غير عمال — دي اللي المفروض ترمي
            var range1 = new FlowRangeDto
            {
                FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 100
            };
            var range2 = new FlowRangeDto
            {
                FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 50
            };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = TestDatabase.BagStage1Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100 }
            };

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, Day1, new[] { range1, range2 }, shares, confirmOverride: true));

            Assert.Contains("مفيش عامل متوزع عليها", ex.Message);
        }

        // ======================= حذف الإنتاج لا يخلّف أشباح =======================

        [Fact]
        public async Task DeletingTheOnlyWorkerRecordOnAStage_RemovesTheNowOrphanedActualOutputRow()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            var record = Assert.Single(await _db.GetProductionAsync());
            Assert.Single(await _db.GetProductionStageOutputsAsync()); // موجود قبل الحذف

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(record.Id, "", "اتسجل بالغلط");

            // مفيش أي سجل عامل باقي لنفس المرحلة/اليوم — الإنتاج الفعلي بقى شبح ويتشال
            Assert.Empty(await _db.GetProductionStageOutputsAsync());
        }

        [Fact]
        public async Task DeletingOneOfTwoWorkersOnTheSameStage_LeavesTheActualOutputRowUntouched()
        {
            // رحلتين منفصلتين، عاملين مختلفين، نفس المرحلة/اليوم — الإنتاج
            // الفعلي بيتجمّع من الاتنين (100 + 50 = 150)
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100,
                TestDatabase.WorkerAhmedId);
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50,
                TestDatabase.WorkerSaidId);

            var ahmedRecord = (await _db.GetProductionAsync())
                .Single(r => r.WorkerId == TestDatabase.WorkerAhmedId);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionAsync(ahmedRecord.Id, "", "اتسجل بالغلط");

            // سعيد لسه له سجل على نفس المرحلة/اليوم — الإنتاج الفعلي (150)
            // ما بيتلمسش خالص، مش بينقص بمقدار قطع أحمد
            var row = Assert.Single(await _db.GetProductionStageOutputsAsync());
            Assert.Equal(150, row.PieceCount);
        }

        [Fact]
        public async Task DeletingTheWholeDay_RemovesActualOutputForEveryStageItTouched()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100);
            Assert.Equal(3, (await _db.GetProductionStageOutputsAsync()).Count); // 3 مراحل

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionDayAsync(Day1, "", "اليوم اتسجل غلط بالكامل");

            Assert.Empty(await _db.GetProductionStageOutputsAsync());
        }
    }
}
