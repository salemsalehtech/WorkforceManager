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
    /// الملف ممكن يطلع بأكتر من شيت حسب طلب المستخدم
    /// (<see cref="ReportExportOptions"/>):
    ///   • الملخص — دايمًا موجود، وهو اللي على الشاشة بالظبط
    ///   • تفاصيل — كل سجل على حدة، عشان يعمل Pivot بنفسه
    ///   • شيت لكل مجموعة — سجلات كل منتج/عامل لوحده، للطباعة والتوزيع
    ///
    /// تنسيق كل عمود بييجي من <see cref="ReportColumn.Kind"/>: الفلوس
    /// بفاصلة آلاف، والكسور بخانتين، والصحيح من غير كسور.
    /// </summary>
    public class ReportTableExcelService
    {
        private static readonly XLColor HeaderColor = XLColor.FromHtml("#1B2E4A");
        private static readonly XLColor AccentColor = XLColor.FromHtml("#C2A14D");
        private static readonly XLColor TotalsColor = XLColor.FromHtml("#F1ECDF");
        private static readonly XLColor StripeColor = XLColor.FromHtml("#FAF7F0");
        private static readonly XLColor MutedColor = XLColor.FromHtml("#5A6779");

        public void Export(
            ReportTable table,
            string filePath,
            ReportDetail? detail = null,
            ReportExportOptions? options = null)
        {
            if (table.IsEmpty)
                throw new InvalidOperationException("مفيش بيانات في التقرير ده للتصدير");

            options ??= new ReportExportOptions();

            using var workbook = new XLWorkbook();

            WriteSummary(workbook, table, options);

            if (options.IncludeDetailSheet && detail is { IsEmpty: false })
                WriteDetail(workbook, workbook.Worksheets.Add(SheetName(detail.SheetName, workbook)), detail, detail.Rows);

            if (options.SheetPerGroup && detail is { IsEmpty: false })
                WriteGroupSheets(workbook, detail);

            workbook.SaveAs(filePath);
        }

        // ======================= شيت الملخص =======================

        private static void WriteSummary(XLWorkbook workbook, ReportTable table, ReportExportOptions options)
        {
            var sheet = workbook.Worksheets.Add(SheetName(table.Title, workbook));
            sheet.RightToLeft = true;

            var lastColumn = table.Columns.Count + 1;
            var row = WriteTitleBlock(sheet, lastColumn, table.Title, table.PeriodText, options);

            // ---------- رؤوس الأعمدة ----------
            var headerRow = row;
            WriteHeader(sheet.Cell(headerRow, 1), table.LabelHeader);

            for (var c = 0; c < table.Columns.Count; c++)
                WriteHeader(sheet.Cell(headerRow, c + 2), table.Columns[c].Header);

            row++;

            // ---------- الصفوف ----------
            foreach (var line in table.Rows)
            {
                sheet.Cell(row, 1).Value = line.Label;

                for (var c = 0; c < table.Columns.Count; c++)
                    WriteValue(sheet.Cell(row, c + 2), Value(line, c), Text(line, c), table.Columns[c].Kind);

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
                    WriteValue(sheet.Cell(row, c + 2), Value(totals, c), Text(totals, c), table.Columns[c].Kind);

                var totalsRange = sheet.Range(row, 1, row, lastColumn);
                totalsRange.Style.Font.SetBold();
                totalsRange.Style.Fill.SetBackgroundColor(TotalsColor);
                totalsRange.Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
            }
            else
            {
                row--; // مفيش سطر إجمالي — آخر سطر هو آخر صف بيانات
            }

            Finish(sheet, headerRow, row, lastColumn, firstColumnWidth: 26);
        }

        // ======================= شيتات التفاصيل =======================

        /// <summary>
        /// شيت لكل مجموعة: سجلات كل منتج (أو عامل) لوحدها.
        ///
        /// بيتبني من نفس صفوف التفاصيل مش من استعلام تاني — يعني
        /// الشيتات دي مجموعها بيساوي شيت التفاصيل بالظبط، ومفيش
        /// احتمال إنهم يقولوا رقمين مختلفين.
        /// </summary>
        private static void WriteGroupSheets(XLWorkbook workbook, ReportDetail detail)
        {
            var groups = detail.Rows
                .GroupBy(r => string.IsNullOrWhiteSpace(r.GroupLabel) ? "بدون تصنيف" : r.GroupLabel!)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                var sheet = workbook.Worksheets.Add(SheetName(group.Key, workbook));
                WriteDetail(workbook, sheet, detail, group.ToList(), group.Key);
            }
        }

        private static void WriteDetail(
            XLWorkbook workbook,
            IXLWorksheet sheet,
            ReportDetail detail,
            IReadOnlyList<ReportDetailRow> rows,
            string? title = null)
        {
            sheet.RightToLeft = true;

            var lastColumn = detail.Columns.Count;
            var row = 1;

            if (title is not null)
            {
                sheet.Range(1, 1, 1, lastColumn).Merge();
                sheet.Cell(1, 1).Value = title;
                sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
                sheet.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                row = 3;
            }

            var headerRow = row;
            for (var c = 0; c < detail.Columns.Count; c++)
                WriteHeader(sheet.Cell(headerRow, c + 1), detail.Columns[c].Header);

            row++;

            foreach (var line in rows)
            {
                for (var c = 0; c < detail.Columns.Count; c++)
                {
                    var cell = c < line.Cells.Count ? line.Cells[c] : default;
                    WriteValue(sheet.Cell(row, c + 1), cell.Number, cell.Text, detail.Columns[c].Kind);
                }

                if (row % 2 == 0)
                    sheet.Range(row, 1, row, lastColumn).Style.Fill.SetBackgroundColor(StripeColor);

                row++;
            }

            // سطر إجمالي للأعمدة اللي بتتجمع — التفاصيل من غيره بتخلي
            // المستخدم يجمع بإيده عشان يتأكد إنها بتطابق الملخص
            var totalsRow = row;
            var hasTotals = false;

            for (var c = 0; c < detail.Columns.Count; c++)
            {
                if (!detail.Columns[c].Sums) continue;

                var sum = rows.Sum(r => c < r.Cells.Count ? r.Cells[c].Number ?? 0m : 0m);
                WriteValue(sheet.Cell(totalsRow, c + 1), sum, null, detail.Columns[c].Kind);
                hasTotals = true;
            }

            if (hasTotals)
            {
                sheet.Cell(totalsRow, 1).Value = "الإجمالي";
                var range = sheet.Range(totalsRow, 1, totalsRow, lastColumn);
                range.Style.Font.SetBold();
                range.Style.Fill.SetBackgroundColor(TotalsColor);
                range.Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
            }
            else
            {
                totalsRow--;
            }

            // فلتر تلقائي على الرأس: ده اللي بيخلي الشيت ده "خام ومرن"
            // فعلاً — المستخدم بيفلتر ويعمل Pivot من غير ما يلمس البرنامج
            sheet.Range(headerRow, 1, totalsRow, lastColumn).SetAutoFilter();

            Finish(sheet, headerRow, totalsRow, lastColumn, firstColumnWidth: 14);
        }

        // ======================= أدوات مشتركة =======================

        /// <summary>بيكتب الشعار والعنوان والمدة، وبيرجع أول سطر بعدهم</summary>
        private static int WriteTitleBlock(
            IXLWorksheet sheet, int lastColumn, string title, string periodText, ReportExportOptions options)
        {
            var row = 1;

            if (!string.IsNullOrWhiteSpace(options.FactoryName))
            {
                sheet.Range(row, 1, row, lastColumn).Merge();
                sheet.Cell(row, 1).Value = options.FactoryName;
                sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(11)
                    .Font.SetFontColor(AccentColor);
                sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                row++;
            }

            sheet.Range(row, 1, row, lastColumn).Merge();
            sheet.Cell(row, 1).Value = title;
            sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(14);
            sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;

            sheet.Range(row, 1, row, lastColumn).Merge();
            sheet.Cell(row, 1).Value = periodText;
            sheet.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Cell(row, 1).Style.Font.SetFontColor(MutedColor);
            row++;

            // الشعار بيتساب لو الملف مش موجود — تقرير من غير شعار أحسن
            // من تصدير بيقع
            if (!string.IsNullOrWhiteSpace(options.LogoPath) && File.Exists(options.LogoPath))
            {
                try
                {
                    sheet.AddPicture(options.LogoPath)
                        .MoveTo(sheet.Cell(1, 1), 4, 4)
                        .WithSize(52, 52);

                    sheet.Row(1).Height = Math.Max(sheet.Row(1).Height, 42);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // صورة تالفة أو صيغة Excel مش فاهمها — التقرير أهم
                }
            }

            return row + 1; // سطر فاضي قبل الجدول
        }

        private static void Finish(
            IXLWorksheet sheet, int headerRow, int lastRow, int lastColumn, double firstColumnWidth)
        {
            var body = sheet.Range(headerRow, 1, lastRow, lastColumn);
            body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            body.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            // تجميد الرأس: الجدول الطويل بيفضل مفهوم وانت نازل فيه
            sheet.SheetView.FreezeRows(headerRow);

            sheet.Columns().AdjustToContents();
            sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, firstColumnWidth);

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0); // العرض في صفحة، والطول زي ما هو
            sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow); // الرأس على كل صفحة مطبوعة
            sheet.PageSetup.Margins.Top = 0.5;
            sheet.PageSetup.Margins.Bottom = 0.5;
            var footer = sheet.PageSetup.Footer.Center;
            footer.AddText("صفحة ");
            footer.AddText(XLHFPredefinedText.PageNumber);
            footer.AddText(" من ");
            footer.AddText(XLHFPredefinedText.NumberOfPages);
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

        private static string? Text(ReportRow row, int index) =>
            index < row.Texts.Count ? row.Texts[index] : null;

        private static void WriteValue(IXLCell cell, decimal? value, string? text, ReportValueKind kind)
        {
            if (value is null)
            {
                if (!string.IsNullOrEmpty(text)) cell.Value = text;
                return;
            }

            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = kind switch
            {
                ReportValueKind.Money => "#,##0",
                ReportValueKind.Fraction => "0.##",
                ReportValueKind.Whole => "#,##0",
                _ => "@"
            };
        }

        /// <summary>
        /// اسم شيت مقبول من Excel: بحد 31 حرف، من غير الحروف الممنوعة،
        /// ومش مكرر — Excel بيرفض الملف كله لو فيه اسمين متشابهين، وده
        /// وارد جدًا مع "شيت لكل مجموعة" (منتجين بأسماء متقاربة).
        /// </summary>
        private static string SheetName(string title, XLWorkbook workbook)
        {
            var clean = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
            if (clean.Length == 0) clean = "تقرير";
            if (clean.Length > 31) clean = clean[..31];

            if (!workbook.Worksheets.Any(w => w.Name.Equals(clean, StringComparison.OrdinalIgnoreCase)))
                return clean;

            for (var i = 2; i < 1000; i++)
            {
                var suffix = $" ({i})";
                var candidate = clean.Length + suffix.Length > 31
                    ? clean[..(31 - suffix.Length)] + suffix
                    : clean + suffix;

                if (!workbook.Worksheets.Any(w => w.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }

            return Guid.NewGuid().ToString("N")[..31];
        }
    }
}
