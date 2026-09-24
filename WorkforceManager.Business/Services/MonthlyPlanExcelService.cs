using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تصدير شاشة الخطة الشهرية لإكسل — **service منفصلة، مش امتداد
    /// لـReportTableExcelService**: تخطيطها (مادة → عيلة → صفوف منتجاتها
    /// → صف subtotal ملوّن → العيلة التالية → صف subtotal المادة) مختلف
    /// عن نموذج ReportTable (صف Totals واحد بس على مستوى الجدول كله)،
    /// فتوسيع ReportTable ليدعم subtotal بمستويين كان هيأثر على كل شاشة
    /// تستخدمها. **بس نفس الأسلوب البصري بالظبط** (الألوان، RTL، عنوان
    /// بالفترة) — عبر الميثودز الـinternal المشتركة في ReportTableExcelService،
    /// مش تكرارهم.
    ///
    /// **الشكل: تخطيط التطبيق النضيف**، مش محاكاة شيت المصنع القديم —
    /// عمود واحد لكل رقم (مش عمودين "تام/داخل" مدمجين زي القديم)، وخطة
    /// العيلة/المادة صف SUM محسوب مالوش أي كتابة يدوية عليه (نفس قاعدة
    /// الشاشة). ملف عادي قابل للتعديل — قيم وتنسيق بس، مفيش حماية/قفل.
    /// </summary>
    public class MonthlyPlanExcelService
    {
        private static readonly string[] Headers =
        {
            "المنتج", "مخطط الشهر", "محقق", "تصليحات", "نسبة المحقق",
            "إنتاج اليوم", "المطلوب يوميًا", "توقّع نهاية الشهر", "الوزن (كجم)"
        };

        public void Export(
            List<MonthlyPlanTrackingDto> tracking, string monthLabel, string filePath,
            ReportExportOptions? options = null)
        {
            if (tracking.Count == 0)
                throw new InvalidOperationException("مفيش بيانات في الخطة الشهرية دي للتصدير");

            options ??= new ReportExportOptions();

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName("الخطة الشهرية", workbook));
            sheet.RightToLeft = true;

            var lastColumn = Headers.Length;
            var row = ReportTableExcelService.WriteTitleBlock(sheet, lastColumn, "الخطة الشهرية", monthLabel, options);

            var headerRow = row;
            for (var c = 0; c < Headers.Length; c++)
                ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, c + 1), Headers[c]);
            row++;

            // مادة (نحاس/زاما/غير محدد) فوق عيلة — نفس ترتيب شيت المصنع الأصلي
            var materialGroups = tracking
                .GroupBy(p => p.Material)
                .OrderBy(g => g.Key switch { Material.Copper => 0, Material.Zamak => 1, _ => 2 })
                .Select(g => (Header: g.Key switch { Material.Copper => "نحاس", Material.Zamak => "زاما", _ => "غير محدد" },
                    Products: g.ToList()))
                .ToList();

            foreach (var (materialHeader, materialProducts) in materialGroups)
            {
                sheet.Range(row, 1, row, lastColumn).Merge();
                sheet.Cell(row, 1).Value = materialHeader;
                sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(XLColor.White);
                sheet.Cell(row, 1).Style.Fill.SetBackgroundColor(ReportTableExcelService.HeaderColor);
                row++;

                var familyGroups = materialProducts
                    .Where(p => p.FamilyId is not null)
                    .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                    .OrderBy(g => g.Key.Item2)
                    .Select(g => (Name: g.Key.Item2, Products: g.ToList()))
                    .ToList();

                var noFamily = materialProducts.Where(p => p.FamilyId is null).ToList();
                if (noFamily.Count > 0) familyGroups.Add(("بدون عيلة", noFamily));

                foreach (var (familyName, products) in familyGroups)
                {
                    sheet.Range(row, 1, row, lastColumn).Merge();
                    sheet.Cell(row, 1).Value = familyName;
                    sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(ReportTableExcelService.AccentColor);
                    row++;

                    foreach (var p in products.OrderBy(p => p.ProductName))
                    {
                        WriteProductRow(sheet, row, p);
                        row++;
                    }

                    WriteSubtotalRow(sheet, row, products);
                    row++;
                }

                WriteSubtotalRow(sheet, row, materialProducts, label: $"إجمالي محقق {materialHeader}");
                row++;
            }

            // إجمالي عام آخر الملف
            WriteSubtotalRow(sheet, row, tracking, label: "الإجمالي العام");
            var lastRow = row;

            ReportTableExcelService.Finish(sheet, headerRow, lastRow, lastColumn, firstColumnWidth: 26);
            workbook.SaveAs(filePath);
        }

        private static void WriteProductRow(IXLWorksheet sheet, int row, MonthlyPlanTrackingDto p)
        {
            sheet.Cell(row, 1).Value = p.ProductName;
            sheet.Cell(row, 2).Value = p.PlannedQuantity;
            sheet.Cell(row, 3).Value = p.EffectiveAchieved;
            sheet.Cell(row, 4).Value = p.CorrectionsToDate;
            sheet.Cell(row, 5).Value = p.AchievedPercent is { } pct ? pct : (double?)null;
            if (p.AchievedPercent is not null) sheet.Cell(row, 5).Style.NumberFormat.Format = "0%";
            sheet.Cell(row, 6).Value = p.TodayCompleted;
            sheet.Cell(row, 7).Value = p.RequiredDailyOutput;
            sheet.Cell(row, 8).Value = p.ForecastEndOfMonth;
            // وزن المحقق بالكيلوجرام — TotalWeightGrams محسوبة (وزن القطعة × المحقق)، null لو المنتج ماله وزن مسجّل
            sheet.Cell(row, 9).Value = p.TotalWeightGrams is { } g ? g / 1000m : (decimal?)null;
            if (p.TotalWeightGrams is not null) sheet.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

            for (var c = 2; c <= 8; c++)
                if (c != 5) sheet.Cell(row, c).Style.NumberFormat.Format = "#,##0";
        }

        private static void WriteSubtotalRow(
            IXLWorksheet sheet, int row, IReadOnlyList<MonthlyPlanTrackingDto> products, string? label = null)
        {
            var plan = products.Sum(p => p.PlannedQuantity);
            var achieved = products.Sum(p => p.EffectiveAchieved);
            var withWeight = products.Where(p => p.TotalWeightGrams is not null).ToList();
            var totalWeightKg = withWeight.Count == 0 ? (decimal?)null : withWeight.Sum(p => p.TotalWeightGrams!.Value) / 1000m;

            sheet.Cell(row, 1).Value = label ?? "إجمالي القسم";
            sheet.Cell(row, 2).Value = plan;
            sheet.Cell(row, 3).Value = achieved;
            // نسبة الإجمالي = محقق ÷ مخطط الشهر كامل (بس لصف الملخص، مش نفس تعريف نسبة المحقق pro-rated لكل صف)
            if (plan > 0) { sheet.Cell(row, 5).Value = (double)achieved / plan; sheet.Cell(row, 5).Style.NumberFormat.Format = "0%"; }
            if (totalWeightKg is not null) { sheet.Cell(row, 9).Value = totalWeightKg; sheet.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00"; }

            var range = sheet.Range(row, 1, row, 9);
            range.Style.Font.SetBold();
            range.Style.Fill.SetBackgroundColor(ReportTableExcelService.TotalsColor);
            sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
            sheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
        }
    }
}
