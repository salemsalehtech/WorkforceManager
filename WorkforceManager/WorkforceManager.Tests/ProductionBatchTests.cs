using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات دفعات الإنتاج — الفكرة اللي كل تقرير "إنتاج مكتمل" قايم
    /// عليها: القطعة بتدخل الخط في دفعة، بتقف عند مرحلة لما اليوم يخلص،
    /// وبتترحّل لبكرة لحد ما تعدّي آخر مرحلة.
    ///
    /// القاعدة اللي الاختبارات دي بتحرسها: **القطعة عمرها ما تتعد مرتين
    /// ولا تظهر من العدم**. أي نطاق بيبدأ من نص الخط لازم يكون بيكمّل دفعة
    /// موجودة، وأي دفعة بتتقسم لازم يفضل مجموع أجزائها = الأصل.
    /// </summary>
    public class ProductionBatchTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => TestDatabase.Today.AddDays(1);
        private static DateTime Day3 => TestDatabase.Today.AddDays(2);

        // ======================= مساعدات =======================

        /// <summary>
        /// يسجل نطاق واحد بعامل واحد على كل مرحلة فيه.
        ///
        /// ملحوظة على اختيار العامل: قاعدة التكليف بتمنع نفس العامل على نفس
        /// المرحلة مرتين في نفس اليوم. فلما اختبار يسجّل دفعتين بيعدّوا على
        /// نفس المرحلة في نفس اليوم، لازم كل دفعة تاخد عامل مختلف.
        /// </summary>
        private async Task<FlowSaveResultDto> RecordAsync(
            DateTime date, int fromStageId, int toStageId, int pieces,
            int[] stageIds, int? batchId = null, int workerId = TestDatabase.WorkerAhmedId,
            bool openingBalance = false)
        {
            var range = new BatchRangeDto
            {
                BatchId = batchId,
                IsOpeningBalance = openingBalance,
                FromStageId = fromStageId,
                ToStageId = toStageId,
                PieceCount = pieces
            };

            var fromIndex = Array.IndexOf(stageIds, fromStageId);
            var toIndex = Array.IndexOf(stageIds, toStageId);

            var shares = new List<FlowShareDto>();
            for (var i = fromIndex; i <= toIndex; i++)
                shares.Add(new FlowShareDto
                {
                    ProductionStageId = stageIds[i],
                    WorkerId = workerId,
                    PieceCount = pieces
                });

            using var scope = _db.CreateScope();
            var flow = _db.GetService<ProductionFlowService>(scope);
            var productId = stageIds[0] == TestDatabase.BagStage1Id
                ? TestDatabase.ProductBagId
                : TestDatabase.ProductRingId;

            // confirmOverride: العامل بيشتغل على أكتر من مرحلة في نفس اليوم
            // عن قصد في الاختبارات دي — قاعدة التكليف مختبرة في ملف تاني
            return await flow.RecordFlowAsync(productId, date, new[] { range }, shares, confirmOverride: true);
        }

        private static readonly int[] BagLine =
        {
            TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id
        };

        private async Task<IReadOnlyList<OpenBatchDto>> OpenBagBatchesAsync(DateTime asOf)
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<ProductionBatchService>(scope);
            return await service.GetOpenBatchesAsync(TestDatabase.ProductBagId, asOf);
        }

        // ======================= فتح الدفعة =======================

        [Fact]
        public async Task Range_from_first_stage_opens_a_new_batch()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);

            var open = await OpenBagBatchesAsync(Day1);

            var batch = Assert.Single(open);
            Assert.Equal(100, batch.Quantity);
            Assert.Equal("تشطيب", batch.NextStageName); // واقفة قبل آخر مرحلة
            Assert.Equal(2, batch.CompletedStages);
            Assert.Equal(3, batch.TotalStages);
        }

        [Fact]
        public async Task Range_covering_whole_line_completes_the_batch_same_day()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 100, BagLine);

            Assert.Empty(await OpenBagBatchesAsync(Day1)); // مفيش واقف

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var batch = await db.ProductionBatches.AsNoTracking().SingleAsync();

            Assert.Equal(BatchStatus.Completed, batch.Status);
            Assert.Equal(Day1.Date, batch.CompletedDate);
            Assert.False(batch.WasCarriedOver); // بدأت وخلصت نفس اليوم
        }

        // ======================= إلزام الربط =======================

        [Fact]
        public async Task Range_starting_mid_line_without_a_batch_is_rejected()
        {
            // من غير الشرط ده، 100 قطعة ممكن تظهر من العدم عند مرحلة 2
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100, BagLine));

            // الرسالة لازم توري المخرجين الاتنين — رسالة "ممنوع" من غير حل
            // بتوقّف المستخدم من غير ما تعلّمه
            Assert.Contains("دفعة واقفة", ex.Message);
            Assert.Contains("رصيد افتتاحي", ex.Message);
        }

        // ======================= الرصيد الافتتاحي =======================

        [Fact]
        public async Task Opening_balance_lets_mid_line_work_start_without_a_prior_batch()
        {
            // الحالة الحقيقية: المصنع شغّال من شهور والشغل واقف في الخط قبل
            // ما نظام الدفعات يبدأ. من غير المخرج ده النظام مينفعش يشتغل يوم 1
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 1000, BagLine,
                openingBalance: true);

            using var scope = _db.CreateScope();
            var report = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day1);

            // عدّت لآخر الخط، فهي إنتاج تام
            Assert.Equal(1000, report.TotalCompletedPieces);
            Assert.Equal(0, report.TotalParkedPieces);
        }

        [Fact]
        public async Task Opening_balance_batch_is_flagged_for_audit()
        {
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 1000, BagLine,
                openingBalance: true);

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var batch = await db.ProductionBatches.AsNoTracking().SingleAsync();

            // مراحلها الأولى مالهاش سجلات إنتاج — العلامة بتفسّر الفرق
            // بدل ما يبان كخطأ في البيانات
            Assert.True(batch.IsOpeningBalance);
            Assert.Contains("رصيد افتتاحي", batch.Notes);
            Assert.Contains("خياطة", batch.Notes); // بتقول دخلت الخط عند فين

            // اتفتحت عند "خياطة" وعدّتها في نفس التسجيل، فواقفة قبل "تشطيب"
            Assert.Equal(TestDatabase.BagStage2Id, batch.LastCompletedStageId);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));
            Assert.Equal("تشطيب", parked.NextStageName);
        }

        [Fact]
        public async Task Opening_balance_batch_carries_and_completes_like_any_other()
        {
            await RecordAsync(Day1, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 1000, BagLine,
                openingBalance: true);

            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));
            Assert.Equal(1000, parked.Quantity);
            Assert.Equal("تشطيب", parked.NextStageName);

            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 1000, BagLine, parked.BatchId);

            using var scope = _db.CreateScope();
            var day2 = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day2);
            Assert.Equal(1000, day2.TotalCompletedPieces);
            Assert.Equal(1000, day2.TotalCarriedInPieces);
        }

        [Fact]
        public async Task Opening_balance_cannot_be_combined_with_an_existing_batch()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // الاتنين مع بعض معناهم "افتح دفعة جديدة وكمّل واحدة قديمة" في
            // نفس الوقت — مدخلات متناقضة
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 100, BagLine,
                    parked.BatchId, openingBalance: true));

            Assert.Contains("مينفعش يتربط بدفعة واقفة", ex.Message);
        }

        [Fact]
        public async Task Continuing_from_the_wrong_stage_is_rejected()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var batch = Assert.Single(await OpenBagBatchesAsync(Day1));

            // الدفعة واقفة عند "خياطة" — تخطّيها للتشطيب معناه قطع ما اتخيطتش
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 100, BagLine, batch.BatchId));

            Assert.Contains("واقفة عند", ex.Message);
        }

        [Fact]
        public async Task Continuing_more_pieces_than_the_batch_holds_is_rejected()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var batch = Assert.Single(await OpenBagBatchesAsync(Day1));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 150, BagLine, batch.BatchId));

            Assert.Contains("100", ex.Message);
        }

        [Fact]
        public async Task Range_from_first_stage_cannot_be_tied_to_an_existing_batch()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var batch = Assert.Single(await OpenBagBatchesAsync(Day1));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day2, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 50, BagLine, batch.BatchId));

            Assert.Contains("دفعة جديدة", ex.Message);
        }

        // ======================= الترحيل لليوم اللي بعده =======================

        [Fact]
        public async Task Batch_carried_to_next_day_completes_and_is_credited_to_the_finish_day()
        {
            // يوم 1: 100 قطعة عدّت مرحلتين ووقفت
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // يوم 2: كمّلت آخر مرحلة
            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 100, BagLine, parked.BatchId);

            using var scope = _db.CreateScope();
            var report = _db.GetService<DailyProductionReportService>(scope);

            // يوم 1: مفيش مكتمل، و100 واقفة
            var day1 = await report.GetAsync(Day1);
            Assert.Equal(0, day1.TotalCompletedPieces);
            Assert.Equal(100, day1.TotalParkedPieces);

            // يوم 2: 100 مكتملة، كلها مرحّلة، ومفيش واقف
            var day2 = await report.GetAsync(Day2);
            Assert.Equal(100, day2.TotalCompletedPieces);
            Assert.Equal(100, day2.TotalCarriedInPieces);
            Assert.Equal(0, day2.TotalParkedPieces);

            var bag = Assert.Single(day2.Products);
            Assert.Equal(0, bag.CompletedSameDayPieces); // ولا قطعة بدأت وخلصت يوم 2
        }

        [Fact]
        public async Task Old_day_report_does_not_change_after_the_batch_finishes_later()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            using (var before = _db.CreateScope())
            {
                var snapshot = await _db.GetService<DailyProductionReportService>(before).GetAsync(Day1);
                Assert.Equal(0, snapshot.TotalCompletedPieces);
            }

            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 100, BagLine, parked.BatchId);

            // تقرير يوم 1 لازم يفضل زي ما كان — تقرير اتطبع مايتغيرش بأثر رجعي
            using var after = _db.CreateScope();
            var day1 = await _db.GetService<DailyProductionReportService>(after).GetAsync(Day1);
            Assert.Equal(0, day1.TotalCompletedPieces);
            Assert.Equal(100, day1.TotalParkedPieces);
        }

        // ======================= التكميل الجزئي (القسمة) =======================

        [Fact]
        public async Task Partial_continuation_splits_the_batch_and_conserves_the_total()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // كمّل 60 بس من الـ100
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 60, BagLine, parked.BatchId);

            var open = await OpenBagBatchesAsync(Day2);
            Assert.Equal(2, open.Count);

            // مجموع الأجزاء = الأصل. لو الرقم ده اتكسر يبقى فيه قطع اتخلقت أو ضاعت
            Assert.Equal(100, open.Sum(b => b.Quantity));

            var advanced = open.Single(b => b.Quantity == 60);
            var leftBehind = open.Single(b => b.Quantity == 40);

            Assert.Equal("تشطيب", advanced.NextStageName);  // اتقدّمت
            Assert.Equal("خياطة", leftBehind.NextStageName); // فضلت مكانها
        }

        [Fact]
        public async Task Split_pieces_keep_the_original_start_date()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage2Id, 60, BagLine, parked.BatchId);

            // القطع المتبقية دخلت الخط يوم 1 مش يوم 2 — التاريخ ده هو اللي
            // بيقول "دي واقفة من كام يوم" لمدير الإنتاج
            var open = await OpenBagBatchesAsync(Day2);
            Assert.All(open, b => Assert.Equal(Day1.Date, b.StartedDate.Date));
        }

        [Fact]
        public async Task Partial_completion_reports_only_the_finished_part()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // 70 خلصوا الخط، 30 لسه واقفين قبل آخر مرحلة
            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 70, BagLine, parked.BatchId);

            using var scope = _db.CreateScope();
            var day2 = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day2);

            Assert.Equal(70, day2.TotalCompletedPieces);
            Assert.Equal(30, day2.TotalParkedPieces);
        }

        // ======================= أكتر من دفعة واقفة =======================

        [Fact]
        public async Task Two_lots_can_wait_at_different_stages_of_the_same_product()
        {
            // دفعة وقفت بعد القص، وتانية وقفت بعد الخياطة — عامل لكل دفعة
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 300, BagLine,
                workerId: TestDatabase.WorkerAhmedId);
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 400, BagLine,
                workerId: TestDatabase.WorkerSaidId);

            var open = await OpenBagBatchesAsync(Day1);

            Assert.Equal(2, open.Count);
            Assert.Equal(300, open.Single(b => b.NextStageName == "خياطة").Quantity);
            Assert.Equal(400, open.Single(b => b.NextStageName == "تشطيب").Quantity);
        }

        [Fact]
        public async Task Same_batch_cannot_be_continued_twice_in_one_save()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            var ranges = new[]
            {
                new BatchRangeDto
                {
                    BatchId = parked.BatchId,
                    FromStageId = TestDatabase.BagStage2Id,
                    ToStageId = TestDatabase.BagStage2Id,
                    PieceCount = 50
                },
                new BatchRangeDto
                {
                    BatchId = parked.BatchId,
                    FromStageId = TestDatabase.BagStage3Id,
                    ToStageId = TestDatabase.BagStage3Id,
                    PieceCount = 50
                }
            };

            var shares = new[]
            {
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage2Id, WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 50 },
                new FlowShareDto { ProductionStageId = TestDatabase.BagStage3Id, WorkerId = TestDatabase.WorkerSaidId, PieceCount = 50 }
            };

            using var scope = _db.CreateScope();
            var flow = _db.GetService<ProductionFlowService>(scope);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                flow.RecordFlowAsync(TestDatabase.ProductBagId, Day2, ranges, shares, confirmOverride: true));

            Assert.Contains("أكتر من نطاق", ex.Message);
        }

        // ======================= الأجور مش بتتأثر =======================

        [Fact]
        public async Task Workers_are_paid_for_the_day_they_actually_worked()
        {
            // يوم 1: مرحلتين × 100 قطعة، اليومية 10 → 10 يوميات لكل مرحلة
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // يوم 2: سعيد كمّل آخر مرحلة
            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 100, BagLine,
                parked.BatchId, TestDatabase.WorkerSaidId);

            var records = await _db.GetProductionAsync();

            // أحمد اشتغل يوم 1 بس، وسعيد يوم 2 بس — الترحيل مبينقلش أجر
            var ahmed = records.Where(r => r.WorkerId == TestDatabase.WorkerAhmedId).ToList();
            var said = records.Where(r => r.WorkerId == TestDatabase.WorkerSaidId).ToList();

            Assert.All(ahmed, r => Assert.Equal(Day1.Date, r.Date.Date));
            Assert.All(said, r => Assert.Equal(Day2.Date, r.Date.Date));
            Assert.Equal(20m, ahmed.Sum(r => r.WorkdaysCompleted)); // مرحلتين × 10 يوميات
            Assert.Equal(10m, said.Sum(r => r.WorkdaysCompleted));
        }

        [Fact]
        public async Task Every_production_row_is_linked_to_its_batch()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 100, BagLine);

            var records = await _db.GetProductionAsync();

            // من غير الربط ده مفيش طريقة نعرف بيها القطع دي راحت فين
            Assert.NotEmpty(records);
            Assert.All(records, r => Assert.NotNull(r.ProductionBatchId));
            Assert.Single(records.Select(r => r.ProductionBatchId).Distinct());
        }

        // ======================= إقفال اليوم =======================

        [Fact]
        public async Task Closing_a_day_blocks_further_production_on_it()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50, BagLine));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task Closed_day_does_not_block_the_next_day()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            // الترحيل هو الهدف كله — الإقفال ميمنعش الشغل يكمّل بكرة
            await RecordAsync(Day2, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id, 100, BagLine, parked.BatchId);

            using var check = _db.CreateScope();
            var day2 = await _db.GetService<DailyProductionReportService>(check).GetAsync(Day2);
            Assert.Equal(100, day2.TotalCompletedPieces);
        }

        [Fact]
        public async Task Reopening_a_day_allows_recording_again()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).ReopenAsync(Day1);

            // غلط الإدخال وارد — حبس المستخدم بره يومه مش حل
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 50, BagLine,
                workerId: TestDatabase.WorkerSaidId);

            var open = await OpenBagBatchesAsync(Day1);
            Assert.Equal(150, open.Sum(b => b.Quantity));
        }

        [Fact]
        public async Task Closing_the_same_day_twice_is_rejected()
        {
            using var scope = _db.CreateScope();
            var closure = _db.GetService<DayClosureService>(scope);
            await closure.CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => closure.CloseAsync(Day1));
            Assert.Contains("مقفول بالفعل", ex.Message);
        }

        [Fact]
        public async Task Closure_preview_lists_what_will_carry_over()
        {
            // دفعتين واقفين على الشنطة، كل واحدة بعاملها
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 300, BagLine,
                workerId: TestDatabase.WorkerAhmedId);
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 400, BagLine,
                workerId: TestDatabase.WorkerSaidId);

            // ودي خلصت الخط كله (منتج بمرحلة واحدة) — مش مفروض تظهر في المرحّل.
            // المعاينة بتغطي المصنع كله مش منتج واحد
            var chainRange = new BatchRangeDto
            {
                FromStageId = TestDatabase.ChainStage1Id,
                ToStageId = TestDatabase.ChainStage1Id,
                PieceCount = 50
            };
            var chainShares = new[]
            {
                new FlowShareDto
                {
                    ProductionStageId = TestDatabase.ChainStage1Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = 50
                }
            };
            using (var write = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(write).RecordFlowAsync(
                    TestDatabase.ProductChainId, Day1, new[] { chainRange }, chainShares, confirmOverride: true);

            using var scope = _db.CreateScope();
            var preview = await _db.GetService<DayClosureService>(scope).PreviewAsync(Day1);

            Assert.Equal(2, preview.CarriedBatchCount);
            Assert.Equal(700, preview.CarriedPieces);
            Assert.Equal(50, preview.CompletedPieces);
            Assert.False(preview.AlreadyClosed);
        }

        // ======================= حماية سجلات الدفعات من التصحيح المباشر =======================

        [Fact]
        public async Task Batch_linked_record_cannot_be_edited_from_the_corrections_tab()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var record = Assert.Single(await _db.GetProductionAsync());

            // تغيير القطع من غير ما الدفعة تتصحّح معاها = تقرير بيكدب في صمت
            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(record.Id, 50));

            Assert.Contains("جزء من دفعة", ex.Message);
        }

        [Fact]
        public async Task Batch_linked_record_cannot_be_deleted_from_the_corrections_tab()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var record = Assert.Single(await _db.GetProductionAsync());

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkdayCalculationService>(scope).DeleteProductionAsync(record.Id));

            Assert.Single(await _db.GetProductionAsync()); // ولا سجل اتشال
        }

        // ======================= الإلغاء (هالك) =======================

        [Fact]
        public async Task Cancelled_batch_leaves_the_parked_list_without_counting_as_output()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionBatchService>(scope).CancelAsync(parked.BatchId, "هالك");

            Assert.Empty(await OpenBagBatchesAsync(Day1));

            using var check = _db.CreateScope();
            var day1 = await _db.GetService<DailyProductionReportService>(check).GetAsync(Day1);
            Assert.Equal(0, day1.TotalCompletedPieces); // اتلغت، مش اتنتجت
            Assert.Equal(0, day1.TotalParkedPieces);

            // بس العامل اشتغل فعلاً — أجره مبيضيعش مع الهالك
            var records = await _db.GetProductionAsync();
            Assert.Equal(10m, records.Sum(r => r.WorkdaysCompleted));
        }

        // ======================= منتج بمرحلة واحدة =======================

        [Fact]
        public async Task Single_stage_product_completes_immediately()
        {
            var range = new BatchRangeDto
            {
                FromStageId = TestDatabase.ChainStage1Id,
                ToStageId = TestDatabase.ChainStage1Id,
                PieceCount = 80
            };
            var shares = new[]
            {
                new FlowShareDto
                {
                    ProductionStageId = TestDatabase.ChainStage1Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = 80
                }
            };

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope)
                .RecordFlowAsync(TestDatabase.ProductChainId, Day1, new[] { range }, shares);

            var report = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day1);

            // أول مرحلة = آخر مرحلة، فالدفعة بتتفتح وتتقفل في نفس النداء
            Assert.Equal(80, report.TotalCompletedPieces);
            Assert.Equal(0, report.TotalParkedPieces);
        }

        // ======================= التقرير =======================

        [Fact]
        public async Task Report_separates_same_day_output_from_carried_output()
        {
            // دفعة وقفت يوم 1
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, 300, BagLine);
            var parked = Assert.Single(await OpenBagBatchesAsync(Day1));

            // يوم 2: كمّلناها + شغلنا دفعة جديدة خلصت في يومها (عامل لكل دفعة)
            await RecordAsync(Day2, TestDatabase.BagStage3Id, TestDatabase.BagStage3Id, 300, BagLine,
                parked.BatchId, TestDatabase.WorkerAhmedId);
            await RecordAsync(Day2, TestDatabase.BagStage1Id, TestDatabase.BagStage3Id, 1000, BagLine,
                workerId: TestDatabase.WorkerSaidId);

            using var scope = _db.CreateScope();
            var day2 = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day2);

            var bag = Assert.Single(day2.Products);
            Assert.Equal(1300, bag.CompletedPieces);
            Assert.Equal(300, bag.CompletedFromCarriedPieces);
            Assert.Equal(1000, bag.CompletedSameDayPieces);
        }

        [Fact]
        public async Task Report_shows_how_long_a_lot_has_been_waiting()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);

            using var scope = _db.CreateScope();
            var day3 = await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day3);

            var lot = Assert.Single(Assert.Single(day3.Products).ParkedLots);
            Assert.Equal(2, lot.DaysWaiting); // واقفة من يومين
            Assert.Equal("خياطة", lot.NextStageName);
        }

        [Fact]
        public async Task Report_marks_the_day_as_closed()
        {
            await RecordAsync(Day1, TestDatabase.BagStage1Id, TestDatabase.BagStage1Id, 100, BagLine);

            using var scope = _db.CreateScope();
            Assert.False((await _db.GetService<DailyProductionReportService>(scope).GetAsync(Day1)).IsClosed);

            await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            using var after = _db.CreateScope();
            var report = await _db.GetService<DailyProductionReportService>(after).GetAsync(Day1);
            Assert.True(report.IsClosed);
            Assert.NotNull(report.ClosedAt);
        }
    }
}
