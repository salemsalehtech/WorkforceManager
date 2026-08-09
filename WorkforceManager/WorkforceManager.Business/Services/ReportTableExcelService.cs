using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيصدّر أي <see cref="ReportTable"/> لملف Excel.
    ///
    /// **مُصدِّر واحد لكل التقارير.** طالما كل المواضيع بتطلع بنفس
    /// الشكل (عنوان + مدة + أعمدة موصوفة + صفوف + إجمالي)، مفيش داعي
    /// لمُصدِّر لكل موضوع. النتيجة إن أي تحسين في الشكل — لون، تجميد
    /// الصف الأول، تنسيق الأرقام، إعداد الطباعة — بيوصل لكل تقرير في
    /// البرنامج مرة واحدة.
    ///
    /// تنسيق كل عمود بييجي من <see cref="ReportColumn.Kind"/>: الفلوس
    /// بفاصلة آلاف، والكسور بخانتين، والصحيح من غير كسور. من غير ده
    /// كان لازم كل تقرير يقول تنسيق أعمدته بنفسه.
    /// </summary>
    public class ReportTableExcelService
    {
        private static readonly XLColor HeaderColor = XLColor.FromHtml("#1F3864");
        private static readonly XLColor TotalsColor = XLColor.FromHtml("#EEF2F9");
        private static readonly XLColor StripeColor = XLColor.FromHtml("#F7F9FC");

        public void Export(ReportTable table, string filePath)
        {
            if (table.IsEmpty)
                throw new InvalidOperationException("مفيش بيانات في التقرير ده للتصدير");

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(SheetName(table.Title));
            sheet.RightToLeft = true;

            var lastColumn = table.Columns.Count + 1;

            // ---------- العنوان والمدة ----------
            sheet.Range(1, 1, 1, lastColumn).Merge();
            sheet.Cell(1, 1).Value = table.Title;
            sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            sheet.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            sheet.Range(2, 1, 2, lastColumn).Merge();
            sheet.Cell(2, 1).Value = table.PeriodText;
            sheet.Cell(2, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#5A6779"));

            // ---------- رؤوس الأعمدة ----------
            const int headerRow = 4;
            WriteHeader(sheet.Cell(headerRow, 1), table.LabelHeader);

            for (var c = 0; c < table.Columns.Count; c++)
                WriteHeader(sheet.Cell(headerRow, c + 2), table.Columns[c].Header);

            // ---------- الصفوف ----------
            var row = headerRow + 1;
            foreach (var line in table.Rows)
            {
                sheet.Cell(row, 1).Value = line.Label;

                for (var c = 0; c < table.Columns.Count; c++)
                    WriteValue(sheet.Cell(row, c + 2), Value(line, c), table.Columns[c].Kind);

                // تظليل الصفوف الزوجية — الجداول الطويلة بتتقري غلط من غيره
                if (row % 2 == 0)
                    sheet.Range(row, 1, row, lastColumn).Style.Fill.SetBackgroundColor(StripeColor);

                row++;
            }

            // ---------- الإجمالي ----------
            if (table.Totals is { } totals)
            {
                sheet.Cell(row, 1).Value = totals.Label;

                for (var c = 0; c < table.Columns.Count; c++)
                    WriteValue(sheet.Cell(row, c + 2), Value(totals, c), table.Columns[c].Kind);

                var totalsRange = sheet.Range(row, 1, row, lastColumn);
                totalsRange.Style.Font.SetBold();
                totalsRange.Style.Fill.SetBackgroundColor(TotalsColor);
                totalsRange.Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
            }

            // ---------- التنسيق النهائي ----------
            var body = sheet.Range(headerRow, 1, row, lastColumn);
            body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            body.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            // تجميد الرأس: الجدول الطويل بيفضل مفهوم وانت نازل فيه
            sheet.SheetView.FreezeRows(headerRow);

            sheet.Columns().AdjustToContents();
            sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 26);

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0); // العرض في صفحة، والطول زي ما هو

            workbook.SaveAs(filePath);
        }

        private static void WriteHeader(IXLCell cell, string text)
        {
            cell.Value = text;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(HeaderColor);
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Alignment.SetWrapText(true);
        }

        private static decimal? Value(ReportRow row, int index) =>
            index < row.Values.Count ? row.Values[index] : null;

        private static void WriteValue(IXLCell cell, decimal? value, ReportValueKind kind)
        {
            if (value is null) return;

            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = kind switch
            {
                ReportValueKind.Money => "#,##0",
                ReportValueKind.Fraction => "0.##",
                ReportValueKind.Whole => "#,##0",
                _ => "@"
            };
        }

        /// <summary>اسم الشيت بحد 31 حرف ومن غير الحروف اللي Excel بيرفضها</summary>
        private static string SheetName(string title)
        {
            var clean = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
            if (clean.Length == 0) clean = "تقرير";
            return clean.Length <= 31 ? clean : clean[..31];
        }
    }
}
