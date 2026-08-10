using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// التصدير لـ Excel — بيتفحص بفتح الملف المتولّد وقراءته، مش
    /// بالتأكد إن الدالة مرمتش.
    ///
    /// السبب: أغلب أعطال التصدير مش استثناءات — ملف بيتكتب "بنجاح"
    /// وبعدين Excel بيرفض يفتحه (اسم شيت مكرر أو ممنوع)، أو بيفتح
    /// وأرقامه نصوص فالجمع والـ Pivot مش شغالين. الاتنين دول بيعدّوا
    /// من أي اختبار بيقيس "مرمتش".
    /// </summary>
    public class ReportExportTests : IDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly List<string> _files = new();

        public void Dispose()
        {
            foreach (var file in _files)
                try { if (File.Exists(file)) File.Delete(file); } catch { /* ملف مؤقت */ }

            _db.Dispose();
        }

        private static DateTime Day => TestDatabase.Today;

        private string NewPath()
        {
            var path = Path.Combine(Path.GetTempPath(), $"wm-test-{Guid.NewGuid():N}.xlsx");
            _files.Add(path);
            return path;
        }

        private async Task RecordAsync(int workerId, int stageId, int pieces)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                workerId, stageId, pieces, Day, confirmOverride: true);
        }

        private async Task<(ReportTable Table, ReportDetail Detail)> BuildAsync(ReportSpec spec)
        {
            using var scope = _db.CreateScope();
            var builder = _db.GetService<ReportBuilderService>(scope);
            return (await builder.BuildAsync(spec), await builder.BuildDetailAsync(spec));
        }

        private static ReportSpec ProductionSpec(ReportGrouping groupBy = ReportGrouping.Worker) => new()
        {
            Subject = ReportSubject.Production,
            GroupBy = groupBy,
            From = Day,
            To = Day
        };

        private string Export(
            ReportTable table, ReportDetail detail, ReportExportOptions options)
        {
            var path = NewPath();
            new ReportTableExcelService().Export(table, path, detail, options);
            return path;
        }

        // ---------------- الشكل الأساسي ----------------

        [Fact]
        public async Task TheExportedFile_OpensAgain_AndItsNumbersAreNumbers()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var (table, detail) = await BuildAsync(ProductionSpec());
            var path = Export(table, detail, new ReportExportOptions());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            // الأرقام لازم تفضل أرقام — لو اتكتبت نص، الجمع والـ Pivot
            // في Excel مش هيشتغلوا والمستخدم مش هيفهم ليه
            var numeric = sheet.CellsUsed(c => c.DataType == XLDataType.Number);
            Assert.NotEmpty(numeric);
        }

        [Fact]
        public async Task WithoutAskingForDetails_TheFileHasOneSheetOnly()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var (table, detail) = await BuildAsync(ProductionSpec());
            var path = Export(table, detail, new ReportExportOptions());

            using var workbook = new XLWorkbook(path);
            Assert.Single(workbook.Worksheets);
        }

        // ---------------- شيت التفاصيل ----------------

        [Fact]
        public async Task TheDetailSheet_HasOneLinePerRecord_NotPerGroup()
        {
            // عاملين، تلات سجلات — الملخص سطرين والتفاصيل تلاتة
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 50);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage3Id, 30);

            var (table, detail) = await BuildAsync(ProductionSpec());
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(3, detail.Rows.Count);

            var path = Export(table, detail, new ReportExportOptions { IncludeDetailSheet = true });

            using var workbook = new XLWorkbook(path);
            Assert.Equal(2, workbook.Worksheets.Count);

            var sheet = workbook.Worksheet("سجلات الإنتاج");
            Assert.NotNull(sheet.AutoFilter); // الفلتر هو اللي بيخلي الشيت مفيد فعلاً
        }

        [Fact]
        public async Task TheDetailSheetTotal_MatchesTheSummaryTotal()
        {
            // لو الشيتين قالوا رقمين مختلفين، المستخدم مش هيعرف يصدّق أنهي واحد
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var (table, detail) = await BuildAsync(ProductionSpec());

            var piecesColumn = table.Columns.FindIndex(c => c.Key == "pieces");
            var summaryTotal = table.Totals!.Values[piecesColumn];

            var detailPieces = detail.Columns.FindIndex(c => c.Key == "pieces");
            var detailTotal = detail.Rows.Sum(r => r.Cells[detailPieces].Number ?? 0);

            Assert.Equal(summaryTotal, detailTotal);
        }

        // ---------------- شيت لكل مجموعة ----------------

        [Fact]
        public async Task SheetPerGroup_MakesOneSheetForEachGroup()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var (table, detail) = await BuildAsync(ProductionSpec());
            var path = Export(table, detail, new ReportExportOptions { SheetPerGroup = true });

            using var workbook = new XLWorkbook(path);

            // الملخص + شيت لأحمد + شيت لسعيد
            Assert.Equal(3, workbook.Worksheets.Count);
            Assert.Contains(workbook.Worksheets, w => w.Name == "أحمد");
            Assert.Contains(workbook.Worksheets, w => w.Name == "سعيد");
        }

        [Fact]
        public async Task TwoGroupsWhoseNamesCollideAfterTrimming_StillProduceAValidFile()
        {
            // Excel بيرفض الملف كله لو فيه اسمين شيت متشابهين — والاسم
            // بيتقص على 31 حرف، فاسمين طويلين متقاربين بيتصادموا
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var (table, detail) = await BuildAsync(ProductionSpec());

            var longName = new string('ط', 40);
            detail.Rows.Clear();
            detail.Rows.Add(new ReportDetailRow
            {
                GroupLabel = longName + "1",
                Cells = { ReportCell.Of(Day), ReportCell.Of("أحمد"), ReportCell.Of("شنطة"),
                          ReportCell.Of("قص"), ReportCell.Of(10m), ReportCell.Of(1m), ReportCell.Of(10m) }
            });
            detail.Rows.Add(new ReportDetailRow
            {
                GroupLabel = longName + "2",
                Cells = { ReportCell.Of(Day), ReportCell.Of("سعيد"), ReportCell.Of("شنطة"),
                          ReportCell.Of("قص"), ReportCell.Of(20m), ReportCell.Of(2m), ReportCell.Of(10m) }
            });

            var path = Export(table, detail, new ReportExportOptions { SheetPerGroup = true });

            using var workbook = new XLWorkbook(path); // بيرمي لو الملف باظ
            Assert.Equal(3, workbook.Worksheets.Count);
            Assert.Equal(
                workbook.Worksheets.Count,
                workbook.Worksheets.Select(w => w.Name).Distinct().Count());
        }

        // ---------------- الشعار ----------------

        [Fact]
        public async Task AMissingLogoFile_DoesNotBreakTheExport()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var (table, detail) = await BuildAsync(ProductionSpec());
            var path = Export(table, detail, new ReportExportOptions
            {
                FactoryName = "مصنع الاختبار",
                LogoPath = @"C:\ملف\مش\موجود.png"
            });

            using var workbook = new XLWorkbook(path);
            Assert.Contains(
                workbook.Worksheets.First().CellsUsed(),
                c => c.GetString().Contains("مصنع الاختبار"));
        }
    }
}
