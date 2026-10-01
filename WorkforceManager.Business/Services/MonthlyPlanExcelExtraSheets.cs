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

    /// <summary>
    /// شيت "اللوحة" — أول شيت في الملف: أهم الأرقام في كروت كبيرة، شريط إنجاز،
    /// العيلات بأشرطة جوه الخلايا (REPT)، وإنتاج كل يوم بشريط. **كله معادلات**
    /// على شيت الخطة الشهرية وشيت يوم بيوم، فلو حد عدّل رقم هناك اللوحة بتتحدّث.
    /// </summary>
    internal static class MonthlyPlanDashboardSheet
    {
        public const string SheetName = "اللوحة";
        private const int BarLength = 30;

        private static readonly XLColor Gold = XLColor.FromHtml("#C2A14D");
        private static readonly XLColor GoldDeep = XLColor.FromHtml("#8A6D1F");
        private static readonly XLColor Ink = XLColor.FromHtml("#342E28");
        private static readonly XLColor Tint = XLColor.FromHtml("#FBF5E4");

        public static void Write(XLWorkbook workbook, MonthlyPlanFactorySheet.Layout layout, string periodText,
            ReportExportOptions options, (int TotalRow, int FirstDayColumn, int DayCount)? daily, DateTime monthStart)
        {
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName(SheetName, workbook));
            sheet.RightToLeft = true;
            sheet.Position = 1;
            sheet.ShowGridLines = false;

            var row = ReportTableExcelService.WriteTitleBlock(sheet, 8, "لوحة الخطة الشهرية", periodText, options);

            var grand = layout.GrandRow;
            var plan = layout.Cell(MonthlyPlanFactorySheet.Layout.Plan, grand);
            var achieved = layout.Cell(MonthlyPlanFactorySheet.Layout.Achieved, grand);

            // ── كروت الأرقام: 4 فوق و4 تحت، كل كارت = تسمية + رقم كبير ──
            var kpis = new (string Label, string Formula, string Format)[]
            {
                ("نسبة الإنجاز", $"IF({plan}=0,\"\",{achieved}/{plan})", "0%"),
                ("المفروض لحد النهارده", "IF(WorkdaysTotal=0,\"\",WorkdaysElapsed/WorkdaysTotal)", "0%"),
                ("الإيقاع", layout.Cell(MonthlyPlanFactorySheet.Layout.ProRated, grand), "0%"),
                ("الأيام الباقية", "WorkdaysRemaining", "0"),
                ("المحقق", achieved, "#,##0"),
                ("خطة الشهر", plan, "#,##0"),
                ("توقّع نهاية الشهر", $"IF(WorkdaysElapsed=0,\"\",ROUND({achieved}/WorkdaysElapsed*WorkdaysTotal,0))", "#,##0"),
                ("المطلوب يوميًا", layout.Cell(MonthlyPlanFactorySheet.Layout.Required, grand), "#,##0"),
            };
            for (var i = 0; i < kpis.Length; i++)
            {
                var r = row + (i / 4) * 3;
                var c = 1 + (i % 4) * 2;
                var label = sheet.Range(r, c, r, c + 1).Merge();
                label.Value = kpis[i].Label;
                label.Style.Font.SetFontColor(XLColor.FromHtml("#625C56")).Font.SetFontSize(10);
                var value = sheet.Range(r + 1, c, r + 1, c + 1).Merge();
                value.FirstCell().FormulaA1 = kpis[i].Formula;
                value.Style.NumberFormat.Format = kpis[i].Format;
                value.Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(i == 0 ? GoldDeep : Ink);
                var card = sheet.Range(r, c, r + 1, c + 1);
                card.Style.Fill.SetBackgroundColor(Tint);
                card.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                card.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(Gold);
                sheet.Row(r + 1).Height = 30;
            }
            row += 6;

            // ── شريط الإنجاز: محقق ▇ مقابل المفروض │ ──
            var completion = $"IF({plan}=0,0,MIN(1,{achieved}/{plan}))";
            sheet.Cell(row, 1).Value = "شريط الإنجاز";
            sheet.Cell(row, 1).Style.Font.SetBold();
            var bar = sheet.Range(row, 2, row, 8).Merge();
            bar.FirstCell().FormulaA1 =
                $"REPT(\"█\",ROUND({completion}*{BarLength * 2},0))&REPT(\"░\",{BarLength * 2}-ROUND({completion}*{BarLength * 2},0))";
            bar.Style.Font.SetFontColor(Gold).Font.SetFontName("Consolas");
            row += 2;

            // ── العيلات ──
            string[] familyHeaders = { "العيلة", "الخطة", "المحقق", "النسبة", "الإنجاز" };
            for (var c = 0; c < familyHeaders.Length; c++)
                ReportTableExcelService.WriteHeader(sheet.Cell(row, c + 1), familyHeaders[c]);
            sheet.Range(row, 5, row, 8).Merge();
            var familyHeader = row;
            row++;
            foreach (var (family, first, last) in layout.Families)
            {
                sheet.Cell(row, 1).Value = family;
                sheet.Cell(row, 2).FormulaA1 = $"SUM({layout.FamilyPlanColumnRange(first, last)})";
                sheet.Cell(row, 3).FormulaA1 = $"SUM({layout.FamilyAchievedColumnRange(first, last)})";
                sheet.Cell(row, 4).FormulaA1 = $"IF(B{row}=0,\"\",C{row}/B{row})";
                var familyBar = sheet.Range(row, 5, row, 8).Merge();
                familyBar.FirstCell().FormulaA1 = $"IF(B{row}=0,\"\",REPT(\"█\",ROUND(MIN(1,C{row}/B{row})*{BarLength},0)))";
                familyBar.Style.Font.SetFontColor(Gold).Font.SetFontName("Consolas");
                sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
                sheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                sheet.Cell(row, 4).Style.NumberFormat.Format = "0%";
                row++;
            }
            if (row > familyHeader + 1)
            {
                var pct = sheet.Range(familyHeader + 1, 4, row - 1, 4);
                pct.AddConditionalFormat().WhenLessThan(0.5).Font.SetFontColor(XLColor.FromHtml("#A0342A"));
                sheet.Range(familyHeader, 1, row - 1, 8).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetInsideBorder(XLBorderStyleValues.Hair);
            }
            row++;

            // ── إنتاج كل يوم بشريط ──
            if (daily is { } d)
            {
                string[] dayHeaders = { "اليوم", "الإنتاج", "" };
                for (var c = 0; c < dayHeaders.Length; c++)
                    ReportTableExcelService.WriteHeader(sheet.Cell(row, c + 1), dayHeaders[c]);
                sheet.Range(row, 3, row, 8).Merge();
                var firstDayRow = row + 1;
                var lastDayRow = firstDayRow + d.DayCount - 1;
                row++;
                for (var i = 0; i < d.DayCount; i++)
                {
                    sheet.Cell(row, 1).Value = monthStart.AddDays(i);
                    sheet.Cell(row, 1).Style.NumberFormat.Format = "dd/mm ddd";
                    var source = $"'{MonthlyPlanDailySheet.SheetName}'!{XLHelper.GetColumnLetterFromNumber(d.FirstDayColumn + i)}{d.TotalRow}";
                    sheet.Cell(row, 2).FormulaA1 = source;
                    sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0;-#,##0;\"\"";
                    var dayBar = sheet.Range(row, 3, row, 8).Merge();
                    dayBar.FirstCell().FormulaA1 =
                        $"IF(MAX($B${firstDayRow}:$B${lastDayRow})=0,\"\",REPT(\"▇\",ROUND(B{row}/MAX($B${firstDayRow}:$B${lastDayRow})*{BarLength},0)))";
                    dayBar.Style.Font.SetFontColor(GoldDeep).Font.SetFontName("Consolas");
                    row++;
                }
            }

            sheet.Column(1).Width = 20;
            for (var c = 2; c <= 8; c++) sheet.Column(c).Width = 12;
            sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.SetTabActive();
        }
    }
}
