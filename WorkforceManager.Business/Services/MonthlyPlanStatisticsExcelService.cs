using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تصدير تبويب "الإحصائيات" لإكسل — 4 شيتات: الملخص، يوم بيوم، العيلات،
    /// المنتجات. نفس الأسلوب البصري لتصدير الخطة الشهرية (ألوان/RTL/عنوان
    /// بالفترة) عبر الميثودز المشتركة في ReportTableExcelService. الأرقام
    /// نفسها من MonthlyPlanStatisticsDto — مفيش حساب هنا غير التنسيق.
    /// </summary>
    public class MonthlyPlanStatisticsExcelService
    {
        private static readonly string[] WeekdayNames =
            { "الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت" };

        public void Export(MonthlyPlanStatisticsDto stats, string periodText, string filePath, ReportExportOptions? options = null)
        {
            if (!stats.HasAnyData)
                throw new InvalidOperationException("مفيش بيانات في الشهر ده للتصدير");

            options ??= new ReportExportOptions();
            using var workbook = new XLWorkbook();

            WriteSummary(workbook, stats, periodText, options);
            WriteDays(workbook, stats, periodText, options);
            WriteFamilies(workbook, stats, periodText, options);
            WriteProducts(workbook, stats, periodText, options);

            workbook.SaveAs(filePath);
        }

        private static void WriteSummary(XLWorkbook workbook, MonthlyPlanStatisticsDto s, string period, ReportExportOptions options)
        {
            var rows = new List<(string Label, object? Value, string? Format)>
            {
                ("أيام الشغل المنقضية", s.ElapsedWorkdays, null),
                ("أيام الشغل الكلية", s.TotalWorkdays, null),
                ("خطة الشهر", s.TotalPlanned, "#,##0"),
                ("المحقق", s.TotalAchieved, "#,##0"),
                ("نسبة الإنجاز من الخطة", s.CompletionPercent, "0%"),
                ("المفروض لحد النهارده", s.ProRatedPlan, "#,##0"),
                ("الإيقاع (المحقق ÷ المفروض)", s.PacePercent, "0%"),
                ("توقّع نهاية الشهر", s.Forecast, "#,##0"),
                ("فرق التوقّع عن الخطة", s.ForecastGap, "#,##0"),
                ("المتوسط اليومي", s.AverageDaily, "#,##0"),
                ("المطلوب يوميًا لباقي الشهر", s.RequiredDaily, "#,##0"),
                ("نفس اليوم الشهر اللي فات", s.PreviousSameDay, "#,##0"),
                ("التغيير عن الشهر اللي فات %", s.ChangeVsPrevious / 100m, "0.0%"),
                ($"متوسط نفس اليوم آخر {s.HistoryMonthsCounted} شهور", s.HistoryAverageSameDay, "#,##0"),
                ("التغيير عن المتوسط %", s.ChangeVsHistoryAverage / 100m, "0.0%"),
                ("الإنتاج الحقيقي (رحلة الإنتاج)", s.TotalRealCompleted, "#,##0"),
                ("الهالك", s.TotalScrap, "#,##0"),
                ("نسبة الهالك", s.ScrapPercent, "0.0%"),
                ("إنتاجية العامل الحاضر / يوم", s.OutputPerPresentWorker, "#,##0.0"),
                ("متوسط الحاضرين", s.AveragePresentWorkers, "#,##0.0"),
                ("الوزن المحقق (كجم)", s.TotalWeightGrams / 1000m, "#,##0.0"),
                ("منتجات سابقة", s.AheadCount, null),
                ("منتجات ماشية صح", s.OnTrackCount, null),
                ("منتجات متأخرة", s.BehindCount, null),
                ("منتجات خارج الخطة", s.OutsidePlanCount, null),
                ("أعلى يوم إنتاج", s.BestDayDate?.ToString("yyyy/MM/dd"), null),
                ("كمية أعلى يوم", s.BestDayQuantity, "#,##0")
            };

            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName("الملخص", workbook));
            sheet.RightToLeft = true;
            var row = ReportTableExcelService.WriteTitleBlock(sheet, 2, "إحصائيات الخطة الشهرية", period, options);
            var headerRow = row;
            ReportTableExcelService.WriteHeader(sheet.Cell(row, 1), "المؤشر");
            ReportTableExcelService.WriteHeader(sheet.Cell(row, 2), "القيمة");
            row++;

            foreach (var (label, value, format) in rows)
            {
                sheet.Cell(row, 1).Value = label;
                SetValue(sheet.Cell(row, 2), value, format);
                row++;
            }

            ReportTableExcelService.Finish(sheet, headerRow, row - 1, 2, firstColumnWidth: 34);
        }

        private static void WriteDays(XLWorkbook workbook, MonthlyPlanStatisticsDto s, string period, ReportExportOptions options)
        {
            string[] headers = { "التاريخ", "اليوم", "يوم شغل", "إنتاج اليوم", "التراكمي", "المفروض تراكميًا", "الشهر اللي فات تراكميًا", "الحاضرين" };
            var sheet = StartTable(workbook, "يوم بيوم", headers, period, options, out var row);
            var headerRow = row - 1;

            foreach (var d in s.Days)
            {
                sheet.Cell(row, 1).Value = d.Date;
                sheet.Cell(row, 1).Style.NumberFormat.Format = "yyyy/mm/dd";
                sheet.Cell(row, 2).Value = WeekdayNames[(int)d.Date.DayOfWeek];
                sheet.Cell(row, 3).Value = d.IsWorkday ? "أيوه" : "إجازة";
                if (!d.IsFuture) SetValue(sheet.Cell(row, 4), d.Quantity, "#,##0");
                SetValue(sheet.Cell(row, 5), d.Cumulative, "#,##0");
                SetValue(sheet.Cell(row, 6), d.PlanCumulative, "#,##0");
                SetValue(sheet.Cell(row, 7), d.PreviousCumulative, "#,##0");
                SetValue(sheet.Cell(row, 8), d.PresentWorkers, null);
                if (!d.IsWorkday) sheet.Range(row, 1, row, headers.Length).Style.Fill.SetBackgroundColor(ReportTableExcelService.TotalsColor);
                row++;
            }

            ReportTableExcelService.Finish(sheet, headerRow, row - 1, headers.Length, firstColumnWidth: 12);
        }

        private static void WriteFamilies(XLWorkbook workbook, MonthlyPlanStatisticsDto s, string period, ReportExportOptions options)
        {
            string[] headers = { "العيلة", "عدد المنتجات", "الخطة", "المحقق", "الإنجاز %", "الإيقاع %", "الوزن (كجم)", "الإنتاج الحقيقي", "الهالك", "نسبة الهالك" };
            var sheet = StartTable(workbook, "العيلات", headers, period, options, out var row);
            var headerRow = row - 1;

            foreach (var f in s.Families)
            {
                sheet.Cell(row, 1).Value = f.FamilyName;
                sheet.Cell(row, 2).Value = f.ProductCount;
                SetValue(sheet.Cell(row, 3), f.Planned, "#,##0");
                SetValue(sheet.Cell(row, 4), f.Achieved, "#,##0");
                SetValue(sheet.Cell(row, 5), f.CompletionPercent, "0%");
                SetValue(sheet.Cell(row, 6), f.PacePercent, "0%");
                SetValue(sheet.Cell(row, 7), f.WeightGrams / 1000m, "#,##0.0");
                SetValue(sheet.Cell(row, 8), f.RealCompleted, "#,##0");
                SetValue(sheet.Cell(row, 9), f.ScrapPieces, "#,##0");
                SetValue(sheet.Cell(row, 10), f.ScrapPercent, "0.0%");
                row++;
            }

            ReportTableExcelService.Finish(sheet, headerRow, row - 1, headers.Length, firstColumnWidth: 22);
        }

        private static void WriteProducts(XLWorkbook workbook, MonthlyPlanStatisticsDto s, string period, ReportExportOptions options)
        {
            string[] headers =
            {
                "المنتج", "العيلة", "الخطة", "المحقق", "الإيقاع %", "المطلوب يوميًا", "توقّع نهاية الشهر",
                "نفس اليوم الشهر اللي فات", "التغيير %", "الإنتاج الحقيقي", "الهالك", "نسبة الهالك"
            };
            var sheet = StartTable(workbook, "المنتجات", headers, period, options, out var row);
            var headerRow = row - 1;

            foreach (var p in s.Products.OrderBy(p => p.FamilyName).ThenBy(p => p.ProductName))
            {
                var real = s.RealByProduct.GetValueOrDefault(p.ProductId) ?? MonthlyPlanStatisticsRealDto.Empty;
                sheet.Cell(row, 1).Value = p.ProductName;
                sheet.Cell(row, 2).Value = p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily;
                SetValue(sheet.Cell(row, 3), p.PlannedQuantity, "#,##0");
                SetValue(sheet.Cell(row, 4), p.EffectiveAchieved, "#,##0");
                SetValue(sheet.Cell(row, 5), p.AchievedPercent, "0%");
                SetValue(sheet.Cell(row, 6), p.PlannedQuantity > 0 ? p.RequiredDailyOutput : null, "#,##0");
                SetValue(sheet.Cell(row, 7), p.ForecastEndOfMonth, "#,##0");
                SetValue(sheet.Cell(row, 8), p.SameDayPreviousMonth, "#,##0");
                SetValue(sheet.Cell(row, 9), ReportBuilderService.PercentChange(p.EffectiveAchieved, p.SameDayPreviousMonth) / 100m, "0.0%");
                SetValue(sheet.Cell(row, 10), real.RealCompleted, "#,##0");
                SetValue(sheet.Cell(row, 11), real.ScrapPieces, "#,##0");
                SetValue(sheet.Cell(row, 12), MonthlyPlanStatisticsMath.ScrapPercent(real.RealCompleted, real.ScrapPieces), "0.0%");
                row++;
            }

            ReportTableExcelService.Finish(sheet, headerRow, row - 1, headers.Length, firstColumnWidth: 26);
        }

        private static IXLWorksheet StartTable(XLWorkbook workbook, string title, string[] headers, string period,
            ReportExportOptions options, out int row)
        {
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName(title, workbook));
            sheet.RightToLeft = true;
            row = ReportTableExcelService.WriteTitleBlock(sheet, headers.Length, title, period, options);
            for (var c = 0; c < headers.Length; c++)
                ReportTableExcelService.WriteHeader(sheet.Cell(row, c + 1), headers[c]);
            row++;
            return sheet;
        }

        private static void SetValue(IXLCell cell, object? value, string? format)
        {
            switch (value)
            {
                case null: return;
                case string text: cell.Value = text; return;
                case int i: cell.Value = i; break;
                case decimal d: cell.Value = d; break;
                case double db: cell.Value = db; break;
                default: cell.Value = value.ToString(); return;
            }
            if (format is not null) cell.Style.NumberFormat.Format = format;
        }
    }
}
