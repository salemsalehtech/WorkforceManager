using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الهالك: القطع اللي اتشالت من الخط ومش هتتكمّل.
    ///
    /// القاعدة كلها في سطر واحد: **المرحلة على سجل الهالك معناها آخر
    /// مرحلة القطعة خلصتها قبل ما تتشال**. التعريف ده هو اللي بيخلي
    /// حالة واحدة تغطي الاتنين:
    ///   • هالك في نص الخط → بيتشال من الشغل الواقف، والتام مايتأثرش
    ///   • هالك على آخر مرحلة → بيتخصم من الإنتاج التام
    ///
    /// ومالوش أي علاقة بالأجور: عمال المراحل الأولى اشتغلوا على القطع
    /// دي فعلًا وخدوا يوميتهم، وعمال المراحل اللي بعدها ماوصلهمش حاجة
    /// فماسجّلوش عليها أصلاً.
    /// </summary>
    public class ScrapTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(int stageId, int pieces, DateTime? date = null)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, stageId, pieces, date ?? Day, confirmOverride: true);
        }

        private Task ScrapAsync(int stageId, int pieces, int? reasonId = null, string? note = null) =>
            _db.InScopeAsync<ScrapService, Core.Models.ProductionScrap>(s =>
                s.RecordAsync(stageId, Day, pieces, reasonId, note));

        private Task<ProductPendingDto?> PendingAsync() =>
            _db.InScopeAsync<PendingWorkService, ProductPendingDto?>(s =>
                s.GetForProductAsync(TestDatabase.ProductBagId, Day));

        private Task<DailyProductionReportDto> DailyAsync() =>
            _db.InScopeAsync<DailyProductionReportService, DailyProductionReportDto>(s => s.GetAsync(Day));

        // ---------------- الهالك في نص الخط ----------------

        [Fact]
        public async Task ScrapMidLine_IsRemovedFromPendingWork()
        {
            // 1000 خلصوا "قص"، و600 بس دخلوا "خياطة" — الفرق 400.
            // 400 منهم هالك، فالواقف يبقى صفر.
            await RecordAsync(TestDatabase.BagStage1Id, 1000);
            await RecordAsync(TestDatabase.BagStage2Id, 600);

            var before = await PendingAsync();
            Assert.Equal(400, before!.Stages.Single(s => s.StageId == TestDatabase.BagStage2Id).PendingPieces);

            await ScrapAsync(TestDatabase.BagStage1Id, 400);

            var after = await PendingAsync();
            Assert.DoesNotContain(after!.Stages, s => s.StageId == TestDatabase.BagStage2Id);
        }

        [Fact]
        public async Task ScrapMidLine_DoesNotChangeCompletedOutput()
        {
            // القطع دي أصلاً ماوصلتش آخر الخط، فالتام مالوش علاقة بيها
            await RecordAsync(TestDatabase.BagStage1Id, 1000);
            await RecordAsync(TestDatabase.BagStage2Id, 600);
            await RecordAsync(TestDatabase.BagStage3Id, 600);

            await ScrapAsync(TestDatabase.BagStage1Id, 400);

            var report = await DailyAsync();
            var bag = report.Products.Single(p => p.ProductId == TestDatabase.ProductBagId);

            Assert.Equal(600, bag.CompletedPieces);
            Assert.Equal(400, bag.ScrapPieces);
        }

        // ---------------- الهالك بعد آخر مرحلة ----------------

        [Fact]
        public async Task ScrapOnTheLastStage_ReducesCompletedOutput()
        {
            // 600 خلصوا الخط كله، الجودة رفضت 100 — الصالح 500.
            // دي الحالة الوحيدة اللي الفرق بين المراحل مش بيكشفها،
            // لأن مفيش مرحلة بعد الأخيرة نقارنها بيها.
            await RecordAsync(TestDatabase.BagStage1Id, 600);
            await RecordAsync(TestDatabase.BagStage2Id, 600);
            await RecordAsync(TestDatabase.BagStage3Id, 600);

            var before = await DailyAsync();
            Assert.Equal(600, before.Products.Single(p => p.ProductId == TestDatabase.ProductBagId).CompletedPieces);

            await ScrapAsync(TestDatabase.BagStage3Id, 100);

            var after = await DailyAsync();
            var bag = after.Products.Single(p => p.ProductId == TestDatabase.ProductBagId);

            Assert.Equal(500, bag.CompletedPieces);
            Assert.Equal(100, bag.ScrapPieces);
        }

        [Fact]
        public async Task ScrapOnTheLastStage_DoesNotCreateFakePendingWork()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 600);
            await RecordAsync(TestDatabase.BagStage2Id, 600);
            await RecordAsync(TestDatabase.BagStage3Id, 600);

            await ScrapAsync(TestDatabase.BagStage3Id, 100);

            var pending = await PendingAsync();
            Assert.Equal(0, pending!.TotalPending);
        }

        // ---------------- التسجيل ----------------

        [Fact]
        public async Task RecordingScrapTwiceOnTheSameStageAndReason_AddsUp_DoesNotSplit()
        {
            // اللي سجّل 300 ونسي 200 عايز يشوف 500، مش سطرين يجمعهم بنفسه
            await ScrapAsync(TestDatabase.BagStage1Id, 300);
            await ScrapAsync(TestDatabase.BagStage1Id, 200);

            var records = await _db.InScopeAsync<ScrapService, IReadOnlyList<ScrapRecordDto>>(
                s => s.GetByDateAsync(Day));

            var record = Assert.Single(records);
            Assert.Equal(500, record.PieceCount);
        }

        [Fact]
        public async Task ScrapWithDifferentReasons_StaysSeparate()
        {
            // "الهالك راح فين؟" سؤال مالوش إجابة لو الأسباب اتجمّعت
            var reasons = await _db.InScopeAsync<ScrapService, List<Core.Models.ScrapReason>>(
                s => s.GetActiveReasonsAsync());

            await ScrapAsync(TestDatabase.BagStage1Id, 300, reasons[0].Id);
            await ScrapAsync(TestDatabase.BagStage1Id, 200, reasons[1].Id);

            var records = await _db.InScopeAsync<ScrapService, IReadOnlyList<ScrapRecordDto>>(
                s => s.GetByDateAsync(Day));

            Assert.Equal(2, records.Count);
            Assert.Equal(500, records.Sum(r => r.PieceCount));
        }

        [Fact]
        public async Task ScrapOfZeroOrLess_IsRefused()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                ScrapAsync(TestDatabase.BagStage1Id, 0));
        }

        // ---------------- الأسباب ----------------

        [Fact]
        public async Task AFreshInstall_ComesWithDefaultReasons()
        {
            var reasons = await _db.InScopeAsync<ScrapService, List<Core.Models.ScrapReason>>(
                s => s.GetActiveReasonsAsync());

            Assert.NotEmpty(reasons);
            Assert.Contains(reasons, r => r.Name == "عيب خامة");
        }

        [Fact]
        public async Task TwoReasonsWithTheSameName_AreRefused()
        {
            // اسمين متشابهين بيقسموا التقرير على نفسه
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<ScrapService, Core.Models.ScrapReason>(
                    s => s.AddReasonAsync("عيب خامة")));
        }

        [Fact]
        public async Task StoppingAReason_HidesItFromNewEntries_ButKeepsOldRecords()
        {
            var reasons = await _db.InScopeAsync<ScrapService, List<Core.Models.ScrapReason>>(
                s => s.GetActiveReasonsAsync());
            var reason = reasons[0];

            await ScrapAsync(TestDatabase.BagStage1Id, 100, reason.Id);

            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).SetReasonActiveAsync(reason.Id, false);

            var active = await _db.InScopeAsync<ScrapService, List<Core.Models.ScrapReason>>(
                s => s.GetActiveReasonsAsync());
            Assert.DoesNotContain(active, r => r.Id == reason.Id);

            // السجل القديم بيفضل بسببه — تقرير الشهر اللي فات لازم يفضل مفهوم
            var records = await _db.InScopeAsync<ScrapService, IReadOnlyList<ScrapRecordDto>>(
                s => s.GetByDateAsync(Day));
            Assert.Equal(reason.Name, records.Single().ReasonName);
        }

        // ---------------- في التقارير ----------------

        [Fact]
        public async Task TheProductionReport_ShowsScrapAndItsRate()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 1000);
            await RecordAsync(TestDatabase.BagStage2Id, 1000);
            await RecordAsync(TestDatabase.BagStage3Id, 900);

            await ScrapAsync(TestDatabase.BagStage3Id, 100);

            var table = await _db.InScopeAsync<ReportBuilderService, ReportTable>(s => s.BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Product,
                From = Day,
                To = Day
            }));

            var row = table.Rows.Single(r => r.Label == "شنطة");
            var scrap = table.Columns.FindIndex(c => c.Key == "scrap");
            var rate = table.Columns.FindIndex(c => c.Key == "scrap_rate");

            Assert.Equal(100, row.Values[scrap]);

            // 100 هالك من 900 خرجت سليمة = 10% من الـ1000 اللي اتشتغل عليها
            Assert.Equal(10m, row.Values[rate]);
        }

        [Fact]
        public async Task TheScrapReport_GroupedByReason_AnswersWhereTheScrapWent()
        {
            var reasons = await _db.InScopeAsync<ScrapService, List<Core.Models.ScrapReason>>(
                s => s.GetActiveReasonsAsync());

            await ScrapAsync(TestDatabase.BagStage1Id, 300, reasons[0].Id);
            await ScrapAsync(TestDatabase.BagStage2Id, 120, reasons[0].Id);
            await ScrapAsync(TestDatabase.BagStage1Id, 80, reasons[1].Id);

            var table = await _db.InScopeAsync<ReportBuilderService, ReportTable>(s => s.BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Scrap,
                GroupBy = ReportGrouping.Reason,
                From = Day,
                To = Day
            }));

            var pieces = table.Columns.FindIndex(c => c.Key == "scrap_pieces");

            // الأكتر هالك الأول — ده ترتيب التقرير الطبيعي
            Assert.Equal(reasons[0].Name, table.Rows[0].Label);
            Assert.Equal(420, table.Rows[0].Values[pieces]);
            Assert.Equal(80, table.Rows[1].Values[pieces]);
            Assert.Equal(500, table.Totals!.Values[pieces]);
        }

        [Fact]
        public async Task TheScrapReport_GroupedByStage_SeparatesEachStage()
        {
            await ScrapAsync(TestDatabase.BagStage1Id, 300);
            await ScrapAsync(TestDatabase.BagStage3Id, 100);

            var table = await _db.InScopeAsync<ReportBuilderService, ReportTable>(s => s.BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Scrap,
                GroupBy = ReportGrouping.Stage,
                From = Day,
                To = Day
            }));

            Assert.Equal(2, table.Rows.Count);
            Assert.Contains(table.Rows, r => r.Label.Contains("قص"));
            Assert.Contains(table.Rows, r => r.Label.Contains("تشطيب"));
        }

        [Fact]
        public async Task GroupedByWorker_TheScrapColumnIsBlank_NotZero()
        {
            // الهالك مالوش عامل، فصفر هنا هيوهم إن العامل ده مالوش هالك
            await RecordAsync(TestDatabase.BagStage1Id, 1000);
            await ScrapAsync(TestDatabase.BagStage1Id, 100);

            var table = await _db.InScopeAsync<ReportBuilderService, ReportTable>(s => s.BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day
            }));

            var scrap = table.Columns.FindIndex(c => c.Key == "scrap");
            Assert.Null(table.Rows[0].Values[scrap]);
        }
    }
}
