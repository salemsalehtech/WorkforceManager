using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// قسايم الأجر: ورقة تتطبع وتتقص بالطول، شريط لكل عامل.
    ///
    /// الاختبارات بتفتح الملف المتولّد وتقراه — أغلب أعطال التصدير مش
    /// استثناءات: ملف بيتكتب "بنجاح" وبعدين Excel يرفض يفتحه، أو
    /// القسيمة تطلع ناقصة سطر، أو الأرقام تبقى نصوص.
    ///
    /// وأهم حاجة هنا: **الرقم اللي في إيد العامل لازم يطابق كشف الأجور
    /// بالحرف** — الورقة دي بتتسلّم مع الفلوس، فأي فرق بينها وبين
    /// الكشف بيتحوّل لخناقة.
    /// </summary>
    public class PayslipStripTests : IDisposable
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
            var path = Path.Combine(Path.GetTempPath(), $"wm-slips-{Guid.NewGuid():N}.xlsx");
            _files.Add(path);
            return path;
        }

        private async Task RecordAsync(int workerId, int stageId, int pieces)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                workerId, stageId, pieces, Day, confirmOverride: true);
        }

        private Task<PeriodPayrollDto> PayrollAsync() =>
            _db.InScopeAsync<PayrollService, PeriodPayrollDto>(s => s.GetPeriodPayrollAsync(Day, Day));

        private string Export(
            PeriodPayrollDto payroll, ReportExportOptions? options = null,
            IReadOnlySet<PayslipStripField>? fields = null)
        {
            var path = NewPath();
            new PayslipStripExcelService().Export(payroll, path, options, fields);
            return path;
        }

        // ---------------- الشكل ----------------

        [Fact]
        public async Task TheFileOpensAgain_AndCarriesTheWorkerName()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            Assert.Contains(sheet.CellsUsed(), c => c.GetString().Contains("أحمد"));
        }

        [Fact]
        public void FourWorkersFitOnOnePage_AndTheFifthStartsANewOne()
        {
            // ورقة A4 بالعرض = 4 قسايم. الخامس بيبدأ ورقة جديدة عشان
            // القص يفضل خطين رأسيين بس على كل ورقة.
            var payroll = new PeriodPayrollDto
            {
                From = Day,
                To = Day,
                Workers = Enumerable.Range(1, 5)
                    .Select(i => new WorkerPayrollDto
                    {
                        WorkerId = i,
                        WorkerName = $"عامل {i}",
                        DailyWageEgp = 200,
                        ProducedWorkdays = 5
                    })
                    .ToList()
            };

            var path = Export(payroll);

            using var workbook = new XLWorkbook(path);
            Assert.Equal(2, workbook.Worksheets.Count);
        }

        [Fact]
        public async Task EverySlipHasTheSameRows_SoTheCutLineStaysStraight()
        {
            // عامل عليه خصومات وعامل نضيف: لازم القسيمتين يبقوا بنفس
            // الطول بالظبط، وإلا المقص هيبقى شغل يدوي لكل قسيمة
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            using (var scope = _db.CreateScope())
                await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerAhmedId, Day, "اتأخر", Core.Enums.PenaltyDeduction.HalfDay);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            // آخر سطر مكتوب فيه حاجة لازم يبقى واحد لكل القسايم
            var lastRowOfSlip1 = LastUsedRow(sheet, column: 1);
            var lastRowOfSlip2 = LastUsedRow(sheet, column: 4);

            Assert.Equal(lastRowOfSlip1, lastRowOfSlip2);
        }

        [Fact]
        public async Task TheFrameCoversTheWholeSlip_DownToTheNetRow()
        {
            // الإطار وخط القص كانوا بيتحسبوا بمعادلة منفصلة عن اللي
            // بيتكتب فعلًا (18 + عدد المراحل)، وكانت غلط بخمس سطور —
            // فالورقة المطبوعة كان إطارها بيقف عند "الحساب" وسايب أجر
            // اليوميات والحوافز والسلف و**الصافي المستحق** برّه.
            // المعادلة اتشالت وبقى الإطار بيتقاس من آخر سطر اتكتب،
            // والاختبار ده هو اللي بيثبّت إن الاتنين مربوطين.
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            var lastContentRow = LastUsedRow(sheet, column: 1);

            Assert.Equal(lastContentRow, LastRowWithBottomFrame(sheet, column: 1));
            Assert.Equal(lastContentRow, LastRowOfCutLine(sheet, gapColumn: 3));
        }

        [Fact]
        public async Task ZeroLinesAreWritten_NotSkipped()
        {
            // عامل مفيش عليه جزاءات ولا سلف: السطور دي لازم تظهر بصفر
            // عشان يشوف بعينه إن مفيش خصومات عليه
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();
            var texts = sheet.CellsUsed().Select(c => c.GetString()).ToList();

            Assert.Contains(texts, t => t.Contains("خصم جزاءات"));
            Assert.Contains(texts, t => t.Contains("سلف"));
            Assert.Contains(texts, t => t.Contains("حوافز"));
        }

        [Fact]
        public async Task TheSlipSaysWhatHeWorkedOn_ProductAndStage()
        {
            // "13,000 قطعة" لوحدها مبتقولش للعامل حاجة — لكن
            // "شنطة/قص 8,000" يقدر يراجعها بنفسه
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, 70);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var texts = workbook.Worksheets.First().CellsUsed()
                .Select(c => c.GetString()).ToList();

            Assert.Contains(texts, t => t.Contains("شنطة") && t.Contains("قص"));
            Assert.Contains(texts, t => t.Contains("دبلة") && t.Contains("تشكيل"));
            Assert.Contains(texts, t => t.Contains("إجمالي القطع"));
        }

        [Fact]
        public async Task WorkersWithDifferentStageCounts_StillGetSlipsOfTheSameLength()
        {
            // أحمد اشتغل على مرحلتين وسعيد على واحدة — لو القسيمتين
            // بطولين مختلفين، خط القص بيبقى شغل يدوي لكل واحدة
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, 70);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            Assert.Equal(LastUsedRow(sheet, column: 1), LastUsedRow(sheet, column: 4));
        }

        [Fact]
        public async Task ThePageIsLandscape_BecauseTheSlipsSitSideBySide()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var path = Export(await PayrollAsync());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        }

        // ---------------- الأرقام ----------------

        [Fact]
        public async Task TheNetOnTheSlip_IsTheSameNumberThePayrollSays()
        {
            // الورقة بتتسلّم مع الفلوس — أي فرق بينها وبين الكشف بيتحوّل
            // لخناقة، فالرقم بيتكتب **كرقم** مأخوذ من نفس الخدمة
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var payroll = await PayrollAsync();
            var worker = payroll.Workers.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);

            var path = Export(payroll);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            var numbers = sheet.CellsUsed(c => c.DataType == XLDataType.Number)
                .Select(c => c.GetDouble())
                .ToList();

            Assert.Contains((double)worker.NetWageEgp, numbers);
        }

        [Fact]
        public async Task TheFactoryNameIsPrintedOnEverySlip_WhenItIsSet()
        {
            // القسيمة بتخرج من البرنامج وتروح لإيد العامل — لازم تقول
            // هي بتاعة مين
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var path = Export(await PayrollAsync(),
                new ReportExportOptions { FactoryName = "مصنع الاختبار" });

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            Assert.Contains(sheet.CellsUsed(), c => c.GetString() == "مصنع الاختبار");
        }

        // ---------------- بنود اختيارية ----------------

        [Fact]
        public async Task WithARestrictedFieldSet_OnlySelectedLinesAppear()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            using (var scope = _db.CreateScope())
                await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerAhmedId, Day, "اتأخر", Core.Enums.PenaltyDeduction.HalfDay);

            // "القسيمة المختصرة" اللي المستخدم طلبها: يوميات + جزاء + حافز + المبلغ النهائي بس
            var fields = new HashSet<PayslipStripField>
            {
                PayslipStripField.NetWorkdays,
                PayslipStripField.PenaltyDeduction,
                PayslipStripField.Bonus
            };

            var path = Export(await PayrollAsync(), fields: fields);

            using var workbook = new XLWorkbook(path);
            var texts = workbook.Worksheets.First().CellsUsed().Select(c => c.GetString()).ToList();

            Assert.Contains(texts, t => t.Contains("صافي اليوميات"));
            Assert.Contains(texts, t => t.Contains("خصم جزاءات"));
            Assert.Contains(texts, t => t.Contains("حوافز"));
            Assert.Contains(texts, t => t.Contains("الصافي المستحق")); // ثابت دايمًا

            // اللي متعلّمش عليه، مش المفروض يظهر خالص
            Assert.DoesNotContain(texts, t => t.Contains("سعر اليومية"));
            Assert.DoesNotContain(texts, t => t.Contains("يوميات منتجة"));
            Assert.DoesNotContain(texts, t => t.Contains("أيام فيها شغل"));
            Assert.DoesNotContain(texts, t => t.Contains("خصم غياب"));
            Assert.DoesNotContain(texts, t => t.Contains("أجر اليوميات"));
            Assert.DoesNotContain(texts, t => t.Contains("سلف"));

            // "اشتغل على" مالهاش أي بند معلّم (لا تفاصيل ولا إجمالي)، فمفروض تختفي بعنوانها كمان
            Assert.DoesNotContain(texts, t => t.Contains("اشتغل على"));
            Assert.DoesNotContain(texts, t => t.Contains("إجمالي القطع"));
        }

        [Fact]
        public async Task WithStageBreakdownHidden_SlipsOfDifferentWorkersStayTheSameLength()
        {
            // نفس اختبار "أطوال مختلفة" الأصلي، بس من غير قسم "اشتغل على" —
            // لازم خط القص يفضل مستقيم حتى لو أهم سبب لاختلاف الطول (عدد
            // المراحل) مش ظاهر في القسيمة أصلًا
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, 70);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var fields = new HashSet<PayslipStripField> { PayslipStripField.NetWorkdays };

            var path = Export(await PayrollAsync(), fields: fields);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            Assert.Equal(LastUsedRow(sheet, column: 1), LastUsedRow(sheet, column: 4));
        }

        [Fact]
        public void AnEmptyPeriod_SaysSoInsteadOfWritingABlankFile()
        {
            var empty = new PeriodPayrollDto { From = Day, To = Day };

            var ex = Assert.Throws<InvalidOperationException>(() => Export(empty));
            Assert.NotEmpty(ex.Message);
        }

        private static int LastUsedRow(IXLWorksheet sheet, int column) =>
            sheet.Column(column).CellsUsed().Any()
                ? sheet.Column(column).CellsUsed().Last().Address.RowNumber
                : 0;

        /// <summary>آخر سطر عليه الحد السفلي للإطار — يعني قاع القسيمة</summary>
        private static int LastRowWithBottomFrame(IXLWorksheet sheet, int column) =>
            LastRowWhere(sheet, r =>
                sheet.Cell(r, column).Style.Border.BottomBorder == XLBorderStyleValues.Medium);

        /// <summary>آخر سطر فيه خط القص المنقّط</summary>
        private static int LastRowOfCutLine(IXLWorksheet sheet, int gapColumn) =>
            LastRowWhere(sheet, r =>
                sheet.Cell(r, gapColumn).Style.Border.LeftBorder == XLBorderStyleValues.Dashed);

        private static int LastRowWhere(IXLWorksheet sheet, Func<int, bool> match)
        {
            // بنعدّي شوية سطور بعد المحتوى كمان: لو الإطار طال أكتر من
            // اللازم ده عيب برضه، والاختبار لازم يشوفه
            var limit = sheet.LastRowUsed()!.RowNumber() + 5;
            var last = 0;

            for (var row = 1; row <= limit; row++)
                if (match(row)) last = row;

            return last;
        }
    }
}
