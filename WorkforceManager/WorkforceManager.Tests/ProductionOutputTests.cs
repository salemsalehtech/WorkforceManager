using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات التام والواقف — الرقمين اللي المصنع بيشتغل عليهم.
    ///
    /// الفكرة اللي الاختبارات دي بتحرسها: **مفيش جدول بيتتبّع القطع**.
    /// التام = إنتاج آخر مرحلة في اليوم. الواقف قبل مرحلة = إجمالي اللي خلص
    /// المرحلة اللي قبلها ناقص إجمالي اللي خلصها هي. القطعة اللي عدّت
    /// السادسة ومعدّتش السابعة هي بالتعريف واقفة قبل السابعة — من غير ما
    /// المستخدم يجاوب على أي سؤال عن "القطع دي جاية منين".
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
            Assert.Equal(0, report.TotalParkedPieces);
        }

        [Fact]
        public async Task Work_that_stops_before_the_last_stage_is_not_completed_output()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100);

            var report = await ReportAsync(Day1);

            Assert.Equal(0, report.TotalCompletedPieces);
            Assert.Equal(100, report.TotalParkedPieces);
        }

        // ======================= الواقف =======================

        [Fact]
        public async Task Waiting_pieces_are_the_gap_between_two_stages()
        {
            // ده الرقم اللي المستخدم شكا منه: أول المراحل عملت 2000 والباقي
            // عمل 1000، فالمفروض يفضل 1000 بس مستنيين — مش 2000
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 2000);
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 1000,
                TestDatabase.WorkerSaidId);

            var bag = Bag(await ReportAsync(Day1));

            Assert.Equal(1000, bag.CompletedPieces);
            Assert.Equal(1000, bag.ParkedPieces);

            var waiting = Assert.Single(bag.StageWip);
            Assert.Equal("خياطة", waiting.StageName);
            Assert.Equal(1000, waiting.WaitingPieces);
        }

        [Fact]
        public async Task Each_stage_shows_its_own_queue()
        {
            // 100 اتقصّت، 70 اتخاطت، 40 اتشطبت
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 70,
                TestDatabase.WorkerSaidId);
            await RecordAsync(Day1, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 40,
                TestDatabase.WorkerAhmedId);

            var bag = Bag(await ReportAsync(Day1));

            Assert.Equal(40, bag.CompletedPieces);
            Assert.Equal(30, bag.StageWip.Single(w => w.StageName == "خياطة").WaitingPieces);
            Assert.Equal(30, bag.StageWip.Single(w => w.StageName == "تشطيب").WaitingPieces);

            // مجموع الأجزاء = اللي دخل الخط. لو الرقم ده اتكسر يبقى فيه قطع
            // اتخلقت أو ضاعت
            Assert.Equal(100, bag.CompletedPieces + bag.ParkedPieces);
        }

        [Fact]
        public async Task Waiting_pieces_carry_across_days_without_any_carry_over_step()
        {
            // مفيش "ترحيل" — الواقف محسوب من أول التسجيل، فبيبان بكرة لوحده
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);

            var day2 = Bag(await ReportAsync(Day2));
            Assert.Equal(100, day2.ParkedPieces);
            Assert.Equal(0, day2.CompletedPieces); // مفيش شغل يوم 2

            // يوم 2: كمّلنا الخط
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100,
                TestDatabase.WorkerSaidId);

            var after = Bag(await ReportAsync(Day2));
            Assert.Equal(100, after.CompletedPieces);
            Assert.Equal(0, after.ParkedPieces);
        }

        [Fact]
        public async Task Old_day_report_does_not_change_after_later_work()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100);
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100,
                TestDatabase.WorkerSaidId);

            // تقرير يوم 1 بيتحسب من سجلات يوم 1 وقبله بس — شغل يوم 2 عمره
            // ما يغيّره بأثر رجعي
            var day1 = Bag(await ReportAsync(Day1));
            Assert.Equal(0, day1.CompletedPieces);
            Assert.Equal(100, day1.ParkedPieces);
        }

        // ======================= أرقام مش منطقية =======================

        [Fact]
        public async Task Stage_recorded_above_the_one_before_it_is_flagged_not_silently_zeroed()
        {
            // مستحيل واقعيًا: 50 اتقصّت و80 اتخاطت. الفرق ده غلط إدخال،
            // وإخفاؤه بتصفير صامت بيخلي المستخدم يبني على رقم غلط
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50);
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 80,
                TestDatabase.WorkerSaidId);

            var bag = Bag(await ReportAsync(Day1));

            var flagged = bag.StageWip.Single(w => w.StageName == "خياطة");
            Assert.True(flagged.IsOverCounted);
            Assert.Equal(30, flagged.OverCountedBy);
            Assert.Equal(0, flagged.WaitingPieces); // الواقف عمره ما يبقى سالب
            Assert.True(bag.HasOverCounting);
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
            Assert.Equal(200, preview.ParkedPieces);
            Assert.False(preview.AlreadyClosed);

            var product = Assert.Single(preview.ParkedByProduct);
            Assert.Equal("شنطة", product.ProductName);
            Assert.Equal("خياطة", product.BiggestQueueStage);
            Assert.Equal(200, product.BiggestQueuePieces);
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

            Assert.Equal(60, Bag(await ReportAsync(Day1)).ParkedPieces);
        }
    }
}
