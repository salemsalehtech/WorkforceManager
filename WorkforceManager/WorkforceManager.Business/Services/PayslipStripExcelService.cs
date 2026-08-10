using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قسايم أجر الأسبوع: ورقة تتطبع وتتقص بالطول، كل شريط قسيمة عامل.
    ///
    /// **الورقة بالعرض (Landscape) و4 عمال فيها.** عرض A4 بالعرض 29.7 سم،
    /// فأربع قسايم بـ6.8 سم للواحدة بتملا الورقة، والقص **خطين رأسيين
    /// بس** — مفيش قص أفقي يخلي الورق يتلخبط. طول الورقة (21 سم) بيستوعب
    /// كل السطور بمساحة مريحة، فمفيش قسيمة بتتزنق.
    ///
    /// **كل القسايم بنفس عدد السطور بالظبط** حتى لو السطر قيمته صفر —
    /// وده اللي بيخلي خط القص مستقيم واحد للصفحة كلها. لو الأصفار اختفت،
    /// كل قسيمة هتبقى بطول مختلف والمقص هيبقى شغل يدوي لكل واحدة.
    /// وكمان العامل بيشوف بعينه إن مفيش خصومات عليه، بدل ما السطر يغيب
    /// فيفتكر إن حد شال منه حاجة.
    ///
    /// الأرقام كلها بتيجي من <see cref="PayrollService"/> — مفيش أي حساب
    /// هنا، عشان القسيمة اللي في إيد العامل تقول نفس اللي كشف الأجور
    /// بيقوله بالحرف.
    /// </summary>
    public class PayslipStripExcelService
    {
        /// <summary>عدد القسايم في الورقة — القص خطين رأسيين بس</summary>
        public const int SlipsPerPage = 4;

        private const int LabelWidth = 17;
        private const int ValueWidth = 11;
        private const int GapWidth = 2;

        private static readonly XLColor HeaderColor = XLColor.FromHtml("#1B2E4A");
        private static readonly XLColor AccentColor = XLColor.FromHtml("#C2A14D");
        private static readonly XLColor NetColor = XLColor.FromHtml("#F1ECDF");
        private static readonly XLColor MutedColor = XLColor.FromHtml("#5A6779");
        private static readonly XLColor CutColor = XLColor.FromHtml("#B0B0B0");

        /// <summary>
        /// أقصى عدد مراحل بتتكتب في القسيمة. اللي بيزيد بيتلمّ في سطر
        /// "ومراحل تانية" — عامل اشتغل على 12 مرحلة قسيمته هتبقى عمود
        /// أرقام مش ورقة يقراها.
        /// </summary>
        private const int MaxBreakdownLines = 6;

        public void Export(
            PeriodPayrollDto payroll, string filePath, ReportExportOptions? options = null)
        {
            if (payroll.Workers.Count == 0)
                throw new InvalidOperationException("مفيش عمال في المدة دي لطباعة قسايمهم");

            options ??= new ReportExportOptions();

            using var workbook = new XLWorkbook();

            // العمال بالاسم: الترتيب ده اللي بيخلي التوزيع سهل — بتدوّر
            // على الاسم في الورق زي ما بتدوّر عليه في أي كشف
            var workers = payroll.Workers.OrderBy(w => w.WorkerName).ToList();

            // **عدد سطور المراحل واحد في كل القسايم.** العامل اللي اشتغل
            // على مرحلة واحدة بياخد نفس المساحة بتاعة اللي اشتغل على
            // أربعة، والناقص بيفضل فاضي — وده اللي بيخلي خط القص مستقيم
            // واحد. من غيره كل قسيمة بطول مختلف والمقص شغل يدوي.
            var breakdownLines = Math.Min(
                MaxBreakdownLines,
                Math.Max(1, workers.Max(w => w.StageBreakdown.Count)));

            var pages = (int)Math.Ceiling(workers.Count / (double)SlipsPerPage);

            for (var page = 0; page < pages; page++)
            {
                var slice = workers.Skip(page * SlipsPerPage).Take(SlipsPerPage).ToList();
                WritePage(workbook, slice, payroll, options, page + 1, pages, breakdownLines);
            }

            workbook.SaveAs(filePath);
        }

        private void WritePage(
            XLWorkbook workbook,
            IReadOnlyList<WorkerPayrollDto> workers,
            PeriodPayrollDto payroll,
            ReportExportOptions options,
            int pageNumber,
            int pageCount,
            int breakdownLines)
        {
            var sheet = workbook.Worksheets.Add(
                pageCount == 1 ? "قسايم الأجر" : $"قسايم {pageNumber}");

            sheet.RightToLeft = true;

            // كل قسيمة عمودين (بيان + قيمة) وبينهم عمود فاصل هو خط القص
            for (var slot = 0; slot < SlipsPerPage; slot++)
            {
                var first = SlotFirstColumn(slot);
                sheet.Column(first).Width = LabelWidth;
                sheet.Column(first + 1).Width = ValueWidth;

                if (slot < SlipsPerPage - 1)
                    sheet.Column(first + 2).Width = GapWidth;
            }

            var lastRow = LastRow(breakdownLines);

            for (var slot = 0; slot < workers.Count; slot++)
                WriteSlip(sheet, workers[slot], payroll, options, slot, breakdownLines, lastRow);

            // الفواصل بين القسايم = خطوط القص. منقّطة عشان تبان إنها
            // خط قص مش حد جدول
            for (var slot = 0; slot < SlipsPerPage - 1; slot++)
            {
                var gap = SlotFirstColumn(slot) + 2;
                sheet.Range(1, gap, lastRow, gap).Style
                    .Border.SetLeftBorder(XLBorderStyleValues.Dashed)
                    .Border.SetLeftBorderColor(CutColor);
            }

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.FitToPages(1, 1); // الورقة كلها في صفحة واحدة
            sheet.PageSetup.Margins.Top = 0.4;
            sheet.PageSetup.Margins.Bottom = 0.4;
            sheet.PageSetup.Margins.Left = 0.3;
            sheet.PageSetup.Margins.Right = 0.3;
            sheet.PageSetup.CenterHorizontally = true;
        }

        /// <summary>أول عمود للقسيمة رقم كذا (عمودين + فاصل)</summary>
        private static int SlotFirstColumn(int slot) => slot * 3 + 1;

        /// <summary>
        /// آخر سطر في القسيمة. بيتحسب من عدد سطور المراحل عشان القسايم
        /// كلها تنتهي عند نفس السطر — ده شرط خط القص المستقيم.
        /// </summary>
        private static int LastRow(int breakdownLines) => 18 + breakdownLines;

        private void WriteSlip(
            IXLWorksheet sheet,
            WorkerPayrollDto worker,
            PeriodPayrollDto payroll,
            ReportExportOptions options,
            int slot,
            int breakdownLines,
            int lastRow)
        {
            var col = SlotFirstColumn(slot);
            var value = col + 1;
            var row = 1;

            // ---------- الرأس: اسم المصنع (لو متحدد) ----------
            if (!string.IsNullOrWhiteSpace(options.FactoryName))
            {
                sheet.Range(row, col, row, value).Merge();
                sheet.Cell(row, col).Value = options.FactoryName;
                sheet.Cell(row, col).Style
                    .Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(AccentColor)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            row++;

            // ---------- اسم العامل ----------
            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value = worker.WorkerName;
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(HeaderColor)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Row(row).Height = 26;
            row++;

            // ---------- الفترة ----------
            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value =
                $"من {payroll.From:yyyy/MM/dd} إلى {payroll.To:yyyy/MM/dd}";
            sheet.Cell(row, col).Style
                .Font.SetFontSize(9).Font.SetFontColor(MutedColor)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;

            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value = worker.IsHourly ? "بالساعة" : "بالقطعة";
            sheet.Cell(row, col).Style
                .Font.SetFontSize(9).Font.SetFontColor(MutedColor)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row += 2;

            // ---------- الشغل ----------
            row = Section(sheet, col, row, "الشغل");
            row = Line(sheet, col, value, row, "سعر اليومية", Money(worker.DailyWageEgp));
            row = Line(sheet, col, value, row, "يوميات منتجة", Number(worker.ProducedWorkdays));
            row = Line(sheet, col, value, row, "أيام فيها شغل", Number(worker.DaysWorked));
            row++;

            // ---------- اشتغل على إيه ----------
            row = Section(sheet, col, row, "اشتغل على");
            row = WriteBreakdown(sheet, col, value, row, worker, breakdownLines);
            row = Line(sheet, col, value, row, "إجمالي القطع", Number(worker.TotalPieces), bold: true);
            row++;

            // ---------- الخصومات ----------
            row = Section(sheet, col, row, "الخصومات");
            row = Line(sheet, col, value, row, "خصم غياب", Number(worker.AbsenceDeduction));
            row = Line(sheet, col, value, row, "خصم جزاءات", Number(worker.PenaltyDeduction));
            row = Line(sheet, col, value, row, "صافي اليوميات", Number(worker.NetWorkdays), bold: true);
            row++;

            // ---------- الفلوس ----------
            row = Section(sheet, col, row, "الحساب");
            row = Line(sheet, col, value, row, "أجر اليوميات", Money(worker.WorkdaysWageEgp));
            row = Line(sheet, col, value, row, "حوافز", Money(worker.BonusEgp));
            row = Line(sheet, col, value, row, "سلف", Money(worker.AdvanceEgp));
            row++;

            // ---------- الصافي المستحق ----------
            sheet.Cell(row, col).Value = "الصافي المستحق";
            sheet.Cell(row, value).Value = worker.NetWageEgp;
            sheet.Cell(row, value).Style.NumberFormat.Format = "#,##0 \"ج\"";

            var net = sheet.Range(row, col, row, value);
            net.Style
                .Font.SetBold().Font.SetFontSize(13)
                .Fill.SetBackgroundColor(NetColor)
                .Border.SetTopBorder(XLBorderStyleValues.Medium)
                .Border.SetBottomBorder(XLBorderStyleValues.Medium)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Row(row).Height = 28;

            // إطار القسيمة كلها — بيوضّح حدود الورقة اللي هتتقص
            sheet.Range(1, col, lastRow, value).Style
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        /// <summary>
        /// المنتج والمرحلة والقطع لكل حتة اشتغل فيها.
        ///
        /// بيكتب **نفس عدد السطور** دايمًا: الناقص بيتساب فاضي عشان خط
        /// القص يفضل مستقيم، والزيادة بتتلمّ في سطر واحد بدل ما القسيمة
        /// تطول وتكسر الصفحة.
        /// </summary>
        private static int WriteBreakdown(
            IXLWorksheet sheet, int col, int valueCol, int row,
            WorkerPayrollDto worker, int lines)
        {
            var shown = worker.StageBreakdown.Take(lines).ToList();
            var hidden = worker.StageBreakdown.Count - shown.Count;

            // آخر سطر متاح بيروح لـ"ومراحل تانية" لو فيه مراحل مخفية،
            // عشان الرقم الناقص ميضيعش من غير ما العامل ياخد باله
            var listed = hidden > 0 ? shown.Take(lines - 1).ToList() : shown;

            foreach (var stage in listed)
                row = Line(sheet, col, valueCol, row, stage.Display, Number(stage.Pieces));

            if (hidden > 0)
            {
                var rest = worker.StageBreakdown.Skip(listed.Count).Sum(s => s.Pieces);
                row = Line(sheet, col, valueCol,
                    row, $"و{worker.StageBreakdown.Count - listed.Count} مراحل تانية", Number(rest));
            }

            // السطور الفاضية بتحجز مكانها عشان القسايم تفضل بنفس الطول
            for (var i = listed.Count + (hidden > 0 ? 1 : 0); i < lines; i++)
                row = Line(sheet, col, valueCol, row, "", "");

            return row;
        }

        /// <summary>عنوان قسم صغير جوه القسيمة</summary>
        private static int Section(IXLWorksheet sheet, int col, int row, string title)
        {
            sheet.Cell(row, col).Value = title;
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(9).Font.SetFontColor(AccentColor);
            return row + 1;
        }

        /// <summary>سطر "بيان: قيمة" — بيتكتب حتى لو القيمة صفر</summary>
        private static int Line(
            IXLWorksheet sheet, int col, int valueCol, int row,
            string label, string value, bool bold = false)
        {
            sheet.Cell(row, col).Value = label;
            sheet.Cell(row, col).Style.Font.SetFontSize(10);

            sheet.Cell(row, valueCol).Value = value;
            sheet.Cell(row, valueCol).Style
                .Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

            if (bold)
            {
                sheet.Cell(row, col).Style.Font.SetBold();
                sheet.Cell(row, valueCol).Style.Font.SetBold();
            }

            sheet.Range(row, col, row, valueCol).Style
                .Border.SetBottomBorder(XLBorderStyleValues.Hair)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#E0E0E0"));

            return row + 1;
        }

        private static string Money(decimal amount) => $"{amount:N0} ج";

        /// <summary>الكسر بيتقص لخانتين، والصحيح بيفضل من غير فاصلة</summary>
        private static string Number(decimal value) => $"{value:0.##}";

        private static string Number(int value) => $"{value:N0}";
    }
}
