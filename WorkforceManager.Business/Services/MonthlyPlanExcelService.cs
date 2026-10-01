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
        // أرقام الأعمدة مسمّاة — لا أرقام سحرية، شوف Headers تحت لنفس الترتيب بالظبط
        private const int ColProduct = 1;
        private const int ColPlan = 2;
        private const int ColAchieved = 3;
        private const int ColCorrections = 4;
        private const int ColAchPercent = 5;      // Ach % — نسبة خام: محقق ÷ مخطط، مش pro-rated
        private const int ColProRatedPercent = 6; // نسبة المحقق — pro-rated لأيام الشهر المنقضية
        private const int ColTodayCompleted = 7;
        private const int ColDailyTarget = 8;
        private const int ColRequiredDaily = 9;
        private const int ColForecast = 10;
        private const int ColWeightKg = 11;

        /// <summary>اسم شيت الجدول التفصيلي (التاني) — الأول هو شيت المصنع</summary>
        public const string DetailSheetName = "تفاصيل";

        private static readonly string[] Headers =
        {
            "المنتج", "مخطط الشهر", "محقق", "تصليحات", "Ach %", "نسبة المحقق",
            "إنتاج اليوم", "الخطة اليومية", "المطلوب يوميًا", "توقّع نهاية الشهر", "الوزن (كجم)"
        };

        public void Export(
            List<MonthlyPlanTrackingDto> tracking, string monthLabel, string filePath,
            ReportExportOptions? options = null, MonthlyPlanDailyBreakdownDto? daily = null)
        {
            if (tracking.Count == 0)
                throw new InvalidOperationException("مفيش بيانات في الخطة الشهرية دي للتصدير");

            options ??= new ReportExportOptions();

            using var workbook = new XLWorkbook();

            // الشيت الأول: نفس أعمدة شيت المصنع وترتيبه بمعادلات حية، شوف MonthlyPlanFactorySheet
            var layout = MonthlyPlanFactorySheet.Write(workbook, tracking, monthLabel, options);

            // الشيت التاني: الجدول التفصيلي (المطلوب يوميًا، التوقّع، الخطة اليومية...) — أرقام مش موجودة في شيت المصنع
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName(DetailSheetName, workbook));
            sheet.RightToLeft = true;

            var lastColumn = Headers.Length;
            var row = ReportTableExcelService.WriteTitleBlock(sheet, lastColumn, "الخطة الشهرية — تفاصيل", monthLabel, options);

            var headerRow = row;
            for (var c = 0; c < Headers.Length; c++)
                ReportTableExcelService.WriteHeader(sheet.Cell(headerRow, c + 1), Headers[c]);
            row++;

            // منتجات خارج الخطة مستبعدة من التجميع الرئيسي — ليها قسمها المستقل تحت، نفس منطق الشاشة بالظبط
            var inPlan = tracking.Where(p => !p.IsOutsidePlan).ToList();
            var outsidePlan = tracking.Where(p => p.IsOutsidePlan).ToList();

            // مادة (نحاس/زاما/غير محدد) فوق عيلة — نفس ترتيب شيت المصنع الأصلي
            var materialGroups = inPlan
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

                    var familyFirstRow = row;
                    foreach (var p in products.OrderBy(p => p.ProductName))
                    {
                        WriteProductRow(sheet, row, p);
                        row++;
                    }

                    // زرار +/− على جنب: تقفل العيلة وتفضل شايف صف إجماليها بس
                    sheet.Rows(familyFirstRow, row - 1).Group();

                    WriteSubtotalRow(sheet, row, products, label: $"إجمالي {familyName}");
                    row++;
                }

                WriteSubtotalRow(sheet, row, materialProducts, label: $"إجمالي محقق {materialHeader}");
                row++;
            }

            // إجمالي عام آخر الملف — من داخل الخطة بس، نفس ما الشاشة بتجمّعه (خارج الخطة قسم مستقل تحت)
            WriteSubtotalRow(sheet, row, inPlan, label: "الإجمالي العام");

            WriteOutsidePlanSection(sheet, ref row, outsidePlan, lastColumn);
            WriteWorkdaysFooter(sheet, ref row, tracking[0]);
            var lastRow = row;

            ReportTableExcelService.Finish(sheet, headerRow, lastRow, lastColumn, firstColumnWidth: 26);

            // يوم بيوم (لو البيانات اتبعتت) واللوحة في الأول — اللوحة معادلات على الشيتين التانيين
            (int, int, int)? dailyLayout = daily is null
                ? null
                : MonthlyPlanDailySheet.Write(workbook, tracking, daily, monthLabel, options);
            MonthlyPlanDashboardSheet.Write(workbook, layout, monthLabel, options, dailyLayout,
                daily is null ? DateTime.Today : new DateTime(daily.Year, daily.Month, 1));

            workbook.SaveAs(filePath);
        }

        private static void WriteProductRow(IXLWorksheet sheet, int row, MonthlyPlanTrackingDto p)
        {
            sheet.Cell(row, ColProduct).Value = p.ProductName;
            sheet.Cell(row, ColPlan).Value = p.PlannedQuantity;
            sheet.Cell(row, ColAchieved).Value = p.EffectiveAchieved;
            sheet.Cell(row, ColCorrections).Value = p.CorrectionsToDate;

            // Ach % — نسبة خام (محقق ÷ مخطط)، مختلفة عمدًا عن نسبة المحقق pro-rated جنبها
            if (p.PlannedQuantity > 0)
            {
                sheet.Cell(row, ColAchPercent).Value = (double)p.EffectiveAchieved / p.PlannedQuantity;
                sheet.Cell(row, ColAchPercent).Style.NumberFormat.Format = "0%";
            }

            sheet.Cell(row, ColProRatedPercent).Value = p.AchievedPercent is { } pct ? pct : (double?)null;
            if (p.AchievedPercent is not null) sheet.Cell(row, ColProRatedPercent).Style.NumberFormat.Format = "0%";

            sheet.Cell(row, ColTodayCompleted).Value = p.TodayCompleted;
            // الخطة اليومية: هدف يدوي، منفصل عن المطلوب يوميًا المحسوب اللي جنبه
            sheet.Cell(row, ColDailyTarget).Value = p.DailyTargetQuantity;
            sheet.Cell(row, ColRequiredDaily).Value = p.RequiredDailyOutput;
            sheet.Cell(row, ColForecast).Value = p.ForecastEndOfMonth;
            // وزن المحقق بالكيلوجرام — TotalWeightGrams محسوبة (وزن القطعة × المحقق)، null لو المنتج ماله وزن مسجّل
            sheet.Cell(row, ColWeightKg).Value = p.TotalWeightGrams is { } g ? g / 1000m : (decimal?)null;
            if (p.TotalWeightGrams is not null) sheet.Cell(row, ColWeightKg).Style.NumberFormat.Format = "#,##0.00";

            for (var c = ColPlan; c <= ColForecast; c++)
                if (c != ColAchPercent && c != ColProRatedPercent) sheet.Cell(row, c).Style.NumberFormat.Format = "#,##0";
        }

        /// <summary>
        /// صف إجمالي — عيلة، مادة، أو إجمالي عام. الإنتاج اليومي المطلوب
        /// هنا نفس صيغة MonthlyPlanFamilyGroupRow.RequiredDailyOutputForFamily
        /// بالحرف (MonthlyPlanFamilyMath.RequiredDailyOutput)، مش نسخة تانية —
        /// الشاشة والتصدير ميقدروش يختلفوا على الرقم ده أبدًا.
        /// </summary>
        private static void WriteSubtotalRow(
            IXLWorksheet sheet, int row, IReadOnlyList<MonthlyPlanTrackingDto> products, string? label = null)
        {
            var plan = products.Sum(p => p.PlannedQuantity);
            var achieved = products.Sum(p => p.EffectiveAchieved);
            var withWeight = products.Where(p => p.TotalWeightGrams is not null).ToList();
            var totalWeightKg = withWeight.Count == 0 ? (decimal?)null : withWeight.Sum(p => p.TotalWeightGrams!.Value) / 1000m;
            var remainingWorkdays = products.Count > 0 ? products[0].RemainingWorkdays : 0;
            var requiredDaily = MonthlyPlanFamilyMath.RequiredDailyOutput(plan, achieved, remainingWorkdays);

            sheet.Cell(row, ColProduct).Value = label ?? "إجمالي القسم";
            sheet.Cell(row, ColPlan).Value = plan;
            sheet.Cell(row, ColAchieved).Value = achieved;
            // Ach % الإجمالي = محقق ÷ مخطط المجموعة كامل (بس لصف الملخص، مش نفس تعريف نسبة المحقق pro-rated لكل صف)
            if (plan > 0)
            {
                sheet.Cell(row, ColAchPercent).Value = (double)achieved / plan;
                sheet.Cell(row, ColAchPercent).Style.NumberFormat.Format = "0%";
            }
            if (requiredDaily is not null) sheet.Cell(row, ColRequiredDaily).Value = requiredDaily.Value;
            if (totalWeightKg is not null) { sheet.Cell(row, ColWeightKg).Value = totalWeightKg; sheet.Cell(row, ColWeightKg).Style.NumberFormat.Format = "#,##0.00"; }

            var range = sheet.Range(row, ColProduct, row, ColWeightKg);
            range.Style.Font.SetBold();
            range.Style.Fill.SetBackgroundColor(ReportTableExcelService.TotalsColor);
            sheet.Cell(row, ColPlan).Style.NumberFormat.Format = "#,##0";
            sheet.Cell(row, ColAchieved).Style.NumberFormat.Format = "#,##0";
            sheet.Cell(row, ColRequiredDaily).Style.NumberFormat.Format = "#,##0";
        }

        /// <summary>منتجات عندها إنتاج بس مفيش خطة مسجّلة — قسم مستقل، نفس شكل قسم "منتجات خارج الخطة" في الشاشة</summary>
        private static void WriteOutsidePlanSection(IXLWorksheet sheet, ref int row, List<MonthlyPlanTrackingDto> outsidePlan, int lastColumn)
        {
            if (outsidePlan.Count == 0) return;

            row++;
            sheet.Range(row, ColProduct, row, lastColumn).Merge();
            sheet.Cell(row, ColProduct).Value = "منتجات خارج الخطة";
            sheet.Cell(row, ColProduct).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(XLColor.White);
            sheet.Cell(row, ColProduct).Style.Fill.SetBackgroundColor(ReportTableExcelService.HeaderColor);
            row++;

            foreach (var p in outsidePlan.OrderBy(p => p.ProductName))
            {
                sheet.Cell(row, ColProduct).Value = p.ProductName;
                sheet.Cell(row, ColAchieved).Value = p.EffectiveAchieved;
                sheet.Cell(row, ColAchieved).Style.NumberFormat.Format = "#,##0";
                if (p.TotalWeightGrams is { } g)
                {
                    sheet.Cell(row, ColWeightKg).Value = g / 1000m;
                    sheet.Cell(row, ColWeightKg).Style.NumberFormat.Format = "#,##0.00";
                }
                row++;
            }
        }

        /// <summary>أيام الشغل الكلية/المنقضية/المتبقية للفترة — من نفس القيمة المحسوبة اللي الشاشة بتعرضها، مش رقم مكتوب هنا</summary>
        private static void WriteWorkdaysFooter(IXLWorksheet sheet, ref int row, MonthlyPlanTrackingDto anyRow)
        {
            row++;
            sheet.Cell(row, ColProduct).Value = "أيام الشغل";
            sheet.Cell(row, ColProduct).Style.Font.SetBold();
            sheet.Cell(row, ColPlan).Value = $"الكلي: {anyRow.TotalWorkdays}";
            sheet.Cell(row, ColAchieved).Value = $"المتبقي: {anyRow.RemainingWorkdays}";
            sheet.Cell(row, ColCorrections).Value = $"المنقضي: {anyRow.TotalWorkdays - anyRow.RemainingWorkdays}";
        }
    }
}
