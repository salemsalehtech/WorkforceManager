using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// شيت "يوم بيوم" في تصدير الخطة الشهرية — صف لكل منتج وعمود لكل يوم،
    /// عشان الشهر كله يبان في نظرة. أيام الإجازة رمادي، والأيام الجاية
    /// فاضية، وتدرّج لوني على الأرقام (الأعلى أغمق) بدل رسم بياني —
    /// ClosedXML مابيعملش رسوم.
    /// </summary>
    internal static class MonthlyPlanDailySheet
    {
        public const string SheetName = "يوم بيوم";

        private static readonly string[] ShortWeekdays = { "حد", "اتنين", "تلات", "أربع", "خميس", "جمعة", "سبت" };

        /// <summary>بيرجع عنوان صف الإجمالي اليومي ونطاق أعمدة الأيام — اللوحة بتقرا منهم</summary>
        public static (int TotalRow, int FirstDayColumn, int DayCount) Write(
            XLWorkbook workbook, List<MonthlyPlanTrackingDto> tracking, MonthlyPlanDailyBreakdownDto daily,
            string periodText, ReportExportOptions options)
        {
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName(SheetName, workbook));
            sheet.RightToLeft = true;

            var start = new DateTime(daily.Year, daily.Month, 1);
            var dayCount = DateTime.DaysInMonth(daily.Year, daily.Month);
            const int colProduct = 1, colFamily = 2, firstDay = 3;
            var colTotal = firstDay + dayCount;

            var row = ReportTableExcelService.WriteTitleBlock(sheet, colTotal, "المحقق يوم بيوم", periodText, options);
            var headerRow = row;
            ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, colProduct), "المنتج");
            ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, colFamily), "العيلة");
            for (var d = 0; d < dayCount; d++)
            {
                var date = start.AddDays(d);
                ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, firstDay + d), $"{date.Day}\n{ShortWeekdays[(int)date.DayOfWeek]}");
            }
            ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, colTotal), "الإجمالي");
            sheet.Row(headerRow).Height = 30;
            row++;

            var products = tracking
                .Where(p => p.PlannedQuantity > 0 || p.EffectiveAchieved != 0)
                .OrderBy(p => p.Material switch { Material.Copper => 0, Material.Zamak => 1, _ => 2 })
                .ThenBy(p => p.FamilyName == null ? 1 : 0).ThenBy(p => p.FamilyName).ThenBy(p => p.ProductName)
                .ToList();

            var firstDataRow = row;
            foreach (var p in products)
            {
                sheet.Cell(row, colProduct).Value = p.ProductName;
                sheet.Cell(row, colFamily).Value = p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily;
                for (var d = 0; d < dayCount; d++)
                {
                    var date = start.AddDays(d);
                    if (date > daily.AsOfDate) continue;
                    if (daily.Quantities.TryGetValue((p.ProductId, date), out var q) && q != 0)
                        sheet.Cell(row, firstDay + d).Value = q;
                }
                sheet.Cell(row, colTotal).FormulaA1 =
                    $"SUM({XLHelper.GetColumnLetterFromNumber(firstDay)}{row}:{XLHelper.GetColumnLetterFromNumber(colTotal - 1)}{row})";
                row++;
            }
            var lastDataRow = row - 1;

            var totalRow = row;
            sheet.Range(totalRow, colProduct, totalRow, colFamily).Merge().Value = "إجمالي اليوم";
            for (var c = firstDay; c <= colTotal; c++)
            {
                var letter = XLHelper.GetColumnLetterFromNumber(c);
                sheet.Cell(totalRow, c).FormulaA1 = lastDataRow >= firstDataRow ? $"SUM({letter}{firstDataRow}:{letter}{lastDataRow})" : "0";
            }
            sheet.Range(totalRow, colProduct, totalRow, colTotal).Style.Font.SetBold()
                .Fill.SetBackgroundColor(ReportTableExcelService.TotalsColor);

            // أيام الإجازة رمادي على العمود كله
            for (var d = 0; d < dayCount; d++)
            {
                var date = start.AddDays(d);
                if (!WorkCalendarRules.IsWorkday(date, daily.Holidays))
                    sheet.Range(headerRow + 1, firstDay + d, totalRow, firstDay + d).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EDEDED"));
            }

            // تدرّج لوني: الأيام الأعلى إنتاجًا لكل المنتجات أغمق — "رسم" جوه الخلايا
            if (lastDataRow >= firstDataRow)
                sheet.Range(firstDataRow, firstDay, lastDataRow, colTotal - 1).AddConditionalFormat()
                    .ColorScale().LowestValue(XLColor.FromHtml("#FFF8E6")).HighestValue(XLColor.FromHtml("#C2A14D"));

            sheet.Range(headerRow + 1, firstDay, totalRow, colTotal).Style.NumberFormat.Format = "#,##0";
            sheet.Range(headerRow + 1, firstDay, totalRow, colTotal).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ReportTableExcelService.Finish(sheet, headerRow, totalRow, colTotal, firstColumnWidth: 22);
            for (var c = firstDay; c < colTotal; c++) sheet.Column(c).Width = 6.5;
            sheet.SheetView.Freeze(headerRow, colFamily);

            return (totalRow, firstDay, dayCount);
        }
    }
}
