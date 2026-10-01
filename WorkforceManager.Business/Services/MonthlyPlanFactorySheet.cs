using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الشيت الأول في تصدير الخطة الشهرية — **نفس أعمدة شيت المصنع وترتيبها**
    /// (العيلة، المنتج، الخطة، المخطط، المحقق، إنتاج اليوم، Ach %، نسبة المحقق،
    /// وزن القطعة، إجمالي الوزن، إجمالي الإنتاج اليومي، تصليحات) بطلب صريح من
    /// المستخدم، عشان الملف يتقري زي ما اتعوّدوا عليه.
    ///
    /// **الأرقام الخام بس هي اللي قيم** (المخطط، المحقق، إنتاج اليوم، وزن القطعة،
    /// التصليحات) — كل الباقي معادلات حية: لو حد عدّل المخطط في الإكسل، النسب
    /// والإجماليات بتتحسب لوحدها. أيام الشغل أسماء معرّفة (WorkdaysTotal/
    /// WorkdaysElapsed) في آخر الشيت، فمعادلة نسبة المحقق مقروءة ومش مربوطة
    /// بمكان خلية.
    ///
    /// **اختلافات مقصودة عن الشيت القديم:**
    ///   • "الخطة" بتاعة العيلة = SUM منتجاتها (قاعدة البرنامج: مفيش خطة عيلة
    ///     بتتكتب لوحدها)، مش رقم يدوي ممكن يختلف عن مجموعها.
    ///   • "إنتاج اليوم" عمود واحد — البرنامج بيسجّل رقم واحد للمنتج في اليوم
    ///     (مش لمعة/صنفرة منفصلين).
    ///   • المحقق = اليدوي + التصليحات (نفس رقم الشاشة)، والتصليحات عمود إفادة.
    ///   • نسبة المحقق = المحقق ÷ (المخطط × المنقضي ÷ الكلي) — نفس تعريف الشاشة.
    /// </summary>
    internal static class MonthlyPlanFactorySheet
    {
        public const string SheetName = "الخطة الشهرية";

        private const int ColFamily = 1;
        private const int ColProduct = 2;
        private const int ColFamilyPlan = 3;
        private const int ColPlan = 4;
        private const int ColAchieved = 5;
        private const int ColToday = 6;
        private const int ColAch = 7;
        private const int ColProRated = 8;
        private const int ColPieceWeight = 9;
        private const int ColTotalWeight = 10;
        private const int ColFamilyToday = 11;
        private const int ColCorrections = 12;
        // أعمدة زيادة بعد ترتيب شيت المصنع — مش موجودة في الشيت القديم
        private const int ColRequired = 13;
        private const int ColPrevious = 14;
        private const int ColChange = 15;
        private const int LastColumn = ColChange;

        private static readonly string[] Headers =
        {
            "العيلة", "المنتج", "الخطة", "المخطط", "المحقق", "إنتاج اليوم", "Ach %", "نسبة المحقق",
            "وزن القطعة (كجم)", "إجمالي الوزن (كجم)", "إجمالي الإنتاج اليومي", "تصليحات",
            "المطلوب يوميًا", "نفس اليوم الشهر اللي فات", "التغيير"
        };

        // ألوان شيت المصنع اللي اتعوّدوا عليها — بس أهدى شوية
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#E2EFDA");
        private static readonly XLColor FamilyFill = XLColor.FromHtml("#D6DCE4");
        private static readonly XLColor DailyFill = XLColor.FromHtml("#FFE699");
        private static readonly XLColor MaterialTotalFill = XLColor.FromHtml("#C6E0B4");
        private static readonly XLColor GrandTotalFill = XLColor.FromHtml("#F8CBAD");
        private static readonly XLColor BehindFill = XLColor.FromHtml("#F8D7D3");
        private static readonly XLColor AheadFill = XLColor.FromHtml("#E2EFDA");

        private const string WholeNumber = "#,##0";
        private const string Percent = "0%";

        public static Layout Write(XLWorkbook workbook, List<MonthlyPlanTrackingDto> tracking, string periodText, ReportExportOptions options)
        {
            var sheet = workbook.Worksheets.Add(ReportTableExcelService.SheetName(SheetName, workbook));
            var familyRows = new List<(string, int, int)>();
            sheet.RightToLeft = true;

            var title = string.IsNullOrWhiteSpace(options.DepartmentName) ? "الخطة الشهرية" : $"قسم {options.DepartmentName!.Trim()}";
            var row = ReportTableExcelService.WriteTitleBlock(sheet, LastColumn, title, periodText, options);

            var headerRow = row;
            for (var c = 0; c < Headers.Length; c++)
            {
                var cell = sheet.Cell(headerRow, c + 1);
                cell.Value = Headers[c];
                cell.Style.Font.SetBold();
                cell.Style.Fill.SetBackgroundColor(HeaderFill);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                cell.Style.Alignment.SetWrapText(true);
            }
            sheet.Row(headerRow).Height = 32;
            row++;

            var inPlan = tracking.Where(p => !p.IsOutsidePlan).ToList();
            var outsidePlan = tracking.Where(p => p.IsOutsidePlan).ToList();
            var firstDataRow = row;

            var materialTotalRows = new List<int>();
            foreach (var material in inPlan
                         .GroupBy(p => p.Material)
                         .OrderBy(g => g.Key switch { Material.Copper => 0, Material.Zamak => 1, _ => 2 }))
            {
                var materialName = material.Key switch { Material.Copper => "النحاس", Material.Zamak => "الزاما", _ => "غير محدد" };
                var materialFirstRow = row;

                var families = material
                    .GroupBy(p => p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily)
                    .OrderBy(g => g.Key == MonthlyPlanStatisticsFilter.NoFamily ? 1 : 0).ThenBy(g => g.Key);

                foreach (var family in families)
                {
                    var familyFirstRow = row;
                    foreach (var p in family.OrderBy(p => p.ProductName))
                    {
                        WriteProductRow(sheet, row, p);
                        row++;
                    }
                    MergeFamilyCells(sheet, family.Key, familyFirstRow, row - 1);
                    familyRows.Add((family.Key, familyFirstRow, row - 1));
                }

                materialTotalRows.Add(row);
                WriteTotalRow(sheet, row, $"إجمالي محقق {materialName}", MaterialTotalFill,
                    col => $"SUM({Ref(col, materialFirstRow)}:{Ref(col, row - 1)})");
                row++;
            }

            var grandRow = row;
            WriteTotalRow(sheet, grandRow, "الإجمالي العام", GrandTotalFill,
                col => materialTotalRows.Count == 0 ? "0" : string.Join("+", materialTotalRows.Select(r => Ref(col, r))));
            row++;

            // تلوين نسبة المحقق: متأخر (< 90%) أحمر هادي، سابق (> 110%) أخضر — نفس حدود الإيقاع في الشاشة
            if (grandRow > firstDataRow)
            {
                var paceRange = sheet.Range(firstDataRow, ColProRated, grandRow, ColProRated);
                paceRange.AddConditionalFormat().WhenBetween(0.0000001, 0.8999999).Fill.SetBackgroundColor(BehindFill);
                paceRange.AddConditionalFormat().WhenGreaterThan(1.1).Fill.SetBackgroundColor(AheadFill);
                sheet.Range(firstDataRow, ColAch, grandRow, ColAch).AddConditionalFormat().DataBar(XLColor.FromHtml("#C2A14D"));
                var change = sheet.Range(firstDataRow, ColChange, grandRow, ColChange);
                change.AddConditionalFormat().WhenLessThan(0).Font.SetFontColor(XLColor.FromHtml("#A0342A"));
                change.AddConditionalFormat().WhenGreaterThan(0).Font.SetFontColor(XLColor.FromHtml("#3A7D2C"));
            }

            var lastTableRow = row - 1;
            WriteOutsidePlan(sheet, ref row, outsidePlan);
            WriteWorkdaysBlock(workbook, sheet, ref row, tracking[0]);

            var body = sheet.Range(headerRow, 1, lastTableRow, LastColumn);
            body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            body.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Range(headerRow + 1, ColFamilyPlan, lastTableRow, LastColumn).Style.Alignment
                .SetHorizontal(XLAlignmentHorizontalValues.Center);

            sheet.SheetView.Freeze(headerRow, ColProduct); // الرأس والعيلة والمنتج ثابتين وانت بتعمل scroll
            sheet.Columns().AdjustToContents();
            sheet.Column(ColFamily).Width = Math.Max(sheet.Column(ColFamily).Width, 18);
            sheet.Column(ColProduct).Width = Math.Max(sheet.Column(ColProduct).Width, 20);
            for (var c = ColFamilyPlan; c <= LastColumn; c++)
                sheet.Column(c).Width = Math.Max(sheet.Column(c).Width, 11); // مفيش "####" ولا 1E+05 زي الشيت القديم

            sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);

            // حماية من غير باسورد: المعادلات مقفولة عشان محدش يمسحها بالغلط، والخانات الخام مفتوحة.
            // اللي عايز يعدّل معادلة: مراجعة ← إلغاء حماية الورقة
            sheet.Protect()
                .AllowElement(XLSheetProtectionElements.FormatColumns)
                .AllowElement(XLSheetProtectionElements.FormatRows)
                .AllowElement(XLSheetProtectionElements.SelectEverything);

            var layout = new Layout { SheetName = sheet.Name, GrandRow = grandRow };
            layout.Families.AddRange(familyRows);
            return layout;
        }

        private static string Ref(int column, int row) => XLHelper.GetColumnLetterFromNumber(column) + row;

        /// <summary>الأرقام الخام — الوحيدة اللي مش مقفولة بعد حماية الشيت</summary>
        private static readonly int[] InputColumns = { ColPlan, ColAchieved, ColToday, ColPieceWeight, ColCorrections, ColPrevious };

        /// <summary>
        /// المطلوب يوميًا = (المخطط − المحقق) ÷ باقي الأيام مقرّب لفوق (نفس MonthlyPlanFamilyMath)،
        /// والتغيير عن نفس اليوم الشهر اللي فات بسهم ▲/▼ من تنسيق الرقم نفسه
        /// </summary>
        private static void WriteRequiredAndChange(IXLWorksheet sheet, int row)
        {
            var plan = Ref(ColPlan, row);
            var achieved = Ref(ColAchieved, row);
            var previous = Ref(ColPrevious, row);
            sheet.Cell(row, ColRequired).FormulaA1 =
                $"IF(OR({plan}=0,WorkdaysRemaining=0),\"\",MAX(0,ROUNDUP(({plan}-{achieved})/WorkdaysRemaining,0)))";
            sheet.Cell(row, ColRequired).Style.NumberFormat.Format = WholeNumber;
            sheet.Cell(row, ColChange).FormulaA1 = $"IF({previous}=0,\"\",{achieved}/{previous}-1)";
            sheet.Cell(row, ColChange).Style.NumberFormat.Format = "▲ 0%;▼ 0%;0%";
        }

        /// <summary>أماكن الصفوف المهمة — شيت "اللوحة" بيبني معادلاته عليها</summary>
        public class Layout
        {
            public string SheetName { get; init; } = "";
            public int GrandRow { get; init; }
            public List<(string Family, int FirstRow, int LastRow)> Families { get; } = new();

            public string Cell(int column, int row) => $"'{SheetName}'!{XLHelper.GetColumnLetterFromNumber(column)}{row}";
            public string PlanCol => XLHelper.GetColumnLetterFromNumber(ColPlan);
            public string AchievedCol => XLHelper.GetColumnLetterFromNumber(ColAchieved);
            public string FamilyPlanColumnRange(int first, int last) =>
                $"'{SheetName}'!{XLHelper.GetColumnLetterFromNumber(ColPlan)}{first}:{XLHelper.GetColumnLetterFromNumber(ColPlan)}{last}";
            public string FamilyAchievedColumnRange(int first, int last) =>
                $"'{SheetName}'!{XLHelper.GetColumnLetterFromNumber(ColAchieved)}{first}:{XLHelper.GetColumnLetterFromNumber(ColAchieved)}{last}";

            public const int Plan = ColPlan, Achieved = ColAchieved, ProRated = ColProRated, Today = ColToday, Required = ColRequired, Weight = ColTotalWeight;
        }

        private static void WriteProductRow(IXLWorksheet sheet, int row, MonthlyPlanTrackingDto p)
        {
            sheet.Cell(row, ColProduct).Value = p.ProductName;
            sheet.Cell(row, ColPlan).Value = p.PlannedQuantity;
            sheet.Cell(row, ColAchieved).Value = p.EffectiveAchieved;
            sheet.Cell(row, ColToday).Value = p.TodayCompleted;
            if (p.PieceWeightGrams is { } grams) sheet.Cell(row, ColPieceWeight).Value = grams / 1000m;
            sheet.Cell(row, ColCorrections).Value = p.CorrectionsToDate;

            var plan = Ref(ColPlan, row);
            var achieved = Ref(ColAchieved, row);
            var weight = Ref(ColPieceWeight, row);
            sheet.Cell(row, ColAch).FormulaA1 = $"IF({plan}=0,\"\",{achieved}/{plan})";
            sheet.Cell(row, ColProRated).FormulaA1 =
                $"IF(OR({plan}=0,WorkdaysElapsed=0),\"\",{achieved}/({plan}*WorkdaysElapsed/WorkdaysTotal))";
            sheet.Cell(row, ColTotalWeight).FormulaA1 = $"IF({weight}=\"\",\"\",{achieved}*{weight})";

            sheet.Cell(row, ColPrevious).Value = p.SameDayPreviousMonth ?? 0;
            WriteRequiredAndChange(sheet, row);

            foreach (var c in new[] { ColPlan, ColAchieved, ColToday, ColCorrections, ColPrevious })
                sheet.Cell(row, c).Style.NumberFormat.Format = WholeNumber;

            // الخانات الخام بس اللي تتعدّل — المعادلات مقفولة (حماية الشيت في Write)
            foreach (var c in InputColumns)
                sheet.Cell(row, c).Style.Protection.SetLocked(false);
            sheet.Cell(row, ColAch).Style.NumberFormat.Format = Percent;
            sheet.Cell(row, ColProRated).Style.NumberFormat.Format = Percent;
            sheet.Cell(row, ColPieceWeight).Style.NumberFormat.Format = "0.000";
            sheet.Cell(row, ColTotalWeight).Style.NumberFormat.Format = "#,##0.0";
        }

        /// <summary>العيلة والخطة وإجمالي الإنتاج اليومي خلايا مدمجة على صفوف العيلة كلها — زي الشيت</summary>
        private static void MergeFamilyCells(IXLWorksheet sheet, string familyName, int firstRow, int lastRow)
        {
            var familyCell = sheet.Range(firstRow, ColFamily, lastRow, ColFamily).Merge();
            familyCell.Value = familyName;
            familyCell.Style.Font.SetBold();
            familyCell.Style.Fill.SetBackgroundColor(FamilyFill);
            familyCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            familyCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            familyCell.Style.Alignment.SetWrapText(true);

            var planCell = sheet.Range(firstRow, ColFamilyPlan, lastRow, ColFamilyPlan).Merge();
            planCell.FirstCell().FormulaA1 = $"SUM({Ref(ColPlan, firstRow)}:{Ref(ColPlan, lastRow)})";
            planCell.Style.Fill.SetBackgroundColor(FamilyFill);
            planCell.Style.Font.SetBold();
            planCell.Style.NumberFormat.Format = WholeNumber;

            var dailyCell = sheet.Range(firstRow, ColFamilyToday, lastRow, ColFamilyToday).Merge();
            dailyCell.FirstCell().FormulaA1 = $"SUM({Ref(ColToday, firstRow)}:{Ref(ColToday, lastRow)})";
            dailyCell.Style.Fill.SetBackgroundColor(DailyFill);
            dailyCell.Style.Font.SetBold();
            dailyCell.Style.NumberFormat.Format = WholeNumber;

            // خط أتقل تحت كل عيلة عشان العيلات تتفصل بالعين
            sheet.Range(lastRow, ColFamily, lastRow, LastColumn).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium);
        }

        /// <summary>صف إجمالي (مادة أو عام) — sumFormula بيرجع معادلة كل عمود رقمي</summary>
        private static void WriteTotalRow(IXLWorksheet sheet, int row, string label, XLColor fill, Func<int, string> sumFormula)
        {
            var labelCell = sheet.Range(row, ColFamily, row, ColProduct).Merge();
            labelCell.Value = label;

            foreach (var c in new[] { ColFamilyPlan, ColPlan, ColAchieved, ColToday, ColTotalWeight, ColFamilyToday, ColCorrections, ColPrevious })
            {
                sheet.Cell(row, c).FormulaA1 = sumFormula(c);
                sheet.Cell(row, c).Style.NumberFormat.Format = c == ColTotalWeight ? "#,##0.0" : WholeNumber;
            }

            var plan = Ref(ColPlan, row);
            var achieved = Ref(ColAchieved, row);
            sheet.Cell(row, ColAch).FormulaA1 = $"IF({plan}=0,\"\",{achieved}/{plan})";
            sheet.Cell(row, ColProRated).FormulaA1 =
                $"IF(OR({plan}=0,WorkdaysElapsed=0),\"\",{achieved}/({plan}*WorkdaysElapsed/WorkdaysTotal))";
            sheet.Cell(row, ColAch).Style.NumberFormat.Format = Percent;
            sheet.Cell(row, ColProRated).Style.NumberFormat.Format = Percent;
            WriteRequiredAndChange(sheet, row);

            var range = sheet.Range(row, ColFamily, row, LastColumn);
            range.Style.Font.SetBold();
            range.Style.Fill.SetBackgroundColor(fill);
        }

        private static void WriteOutsidePlan(IXLWorksheet sheet, ref int row, List<MonthlyPlanTrackingDto> outsidePlan)
        {
            if (outsidePlan.Count == 0) return;

            row++;
            var header = sheet.Range(row, ColFamily, row, LastColumn).Merge();
            header.Value = "منتجات خارج الخطة — عندها إنتاج بس مفيش خطة";
            header.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            header.Style.Fill.SetBackgroundColor(ReportTableExcelService.HeaderColor);
            row++;

            foreach (var p in outsidePlan.OrderBy(p => p.ProductName))
            {
                sheet.Cell(row, ColFamily).Value = p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily;
                sheet.Cell(row, ColProduct).Value = p.ProductName;
                sheet.Cell(row, ColAchieved).Value = p.EffectiveAchieved;
                sheet.Cell(row, ColToday).Value = p.TodayCompleted;
                sheet.Cell(row, ColAchieved).Style.NumberFormat.Format = WholeNumber;
                sheet.Cell(row, ColToday).Style.NumberFormat.Format = WholeNumber;
                row++;
            }
        }

        /// <summary>
        /// صندوق أيام الشغل آخر الشيت — الكلي والمنقضي قيم من نفس حساب الشاشة،
        /// والباقي معادلة. أول اتنين أسماء معرّفة بتستخدمها معادلة نسبة المحقق.
        /// </summary>
        private static void WriteWorkdaysBlock(XLWorkbook workbook, IXLWorksheet sheet, ref int row, MonthlyPlanTrackingDto anyRow)
        {
            var line = row + 1;
            var elapsed = anyRow.TotalWorkdays - anyRow.RemainingWorkdays;

            void Line(string label, Action<IXLCell> value, string? name)
            {
                var labelCell = sheet.Range(line, ColFamily, line, ColProduct).Merge();
                labelCell.Value = label;
                labelCell.Style.Font.SetBold();
                labelCell.Style.Fill.SetBackgroundColor(DailyFill);
                var cell = sheet.Cell(line, ColFamilyPlan);
                value(cell);
                cell.Style.Font.SetBold();
                cell.Style.Fill.SetBackgroundColor(DailyFill);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                sheet.Range(line, ColFamily, line, ColFamilyPlan).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                if (name is not null) workbook.DefinedNames.Add(name, cell.AsRange());
                line++;
            }

            Line("إجمالي أيام الشغل", c => c.Value = anyRow.TotalWorkdays, "WorkdaysTotal");
            Line("أيام الشغل", c => c.Value = elapsed, "WorkdaysElapsed");
            Line("باقي الأيام", c => c.FormulaA1 = "WorkdaysTotal-WorkdaysElapsed", "WorkdaysRemaining");
            row = line;
        }
    }
}
