using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بند اختياري في قسيمة الأجر المطبوعة — كل قيمة سطر أو مجموعة سطور
    /// المستخدم يقدر يظهرها/يخفيها من شاشة "التقارير". اسم المصنع
    /// والعامل والفترة و"الصافي المستحق" ثابتين دايمًا، مش جزء من القائمة
    /// دي — قسيمة من غير رقم نهائي مالهاش معنى.
    /// </summary>
    public enum PayslipStripField
    {
        DailyWageRate,
        ProducedWorkdays,
        DaysWorked,
        StageBreakdown,
        TotalPieces,
        AbsenceDeduction,
        PenaltyDeduction,
        NetWorkdays,
        WorkdaysWageEgp,
        Bonus,
        Advance
    }

    /// <summary>
    /// قسايم أجر الأسبوع: ورقة تتطبع وتتقص لشرايط، شريط قسيمة عامل.
    ///
    /// **متصمّمة للطباعة أبيض وأسود.** الورقة دي بتتطبع على طابعة القسم
    /// مش بتتبعت بالإيميل، فأي لون بيتحوّل لدرجة رمادي: الدهبي بيبقى
    /// رمادي فاتح مبيتشافش، والرمادي الفاتح بيختفي خالص. عشان كده كل
    /// النص **أسود**، والتمييز بالخط العريض والحجم والإطارات — مش باللون.
    ///
    /// **الورقة بالعرض (Landscape) و8 قسايم فيها: صفين × 4 أعمدة.**
    /// اتغيّرت من 4 لـ8 (صف واحد) بناءً على طلب المستخدم لتوفير الورق —
    /// نفس عرض العمود القديم اتحافظ عليه (بمقاس 100% أفقيًا)، بس ارتفاع
    /// القسيمة الواحدة اتقلّل (خطوط/فجوات/عدد سطور مراحل أقل) عشان صفين
    /// يدخلوا في ارتفاع الورقة. FitToPages(1,1) هو خط الدفاع الأخير لو
    /// المحتوى لسه أطول من المتاح — بيصغّر الكل بنفس النسبة، فالعمود
    /// يفضل مصطف حتى لو التصغير حصل.
    ///
    /// **الأرقام بتتكتب أرقام مش نصوص**، والوحدة ("ج") جاية من تنسيق
    /// الخلية. كده الأرقام بتصطف تحت بعضها في عمود مستقيم — النص
    /// "200 ج" و"0 ج" بيبقوا بعرض مختلف فبيطلعوا مبعثرين.
    ///
    /// **كل القسايم بنفس عدد السطور بالظبط** حتى لو السطر قيمته صفر —
    /// وده اللي بيخلي خط القص مستقيم واحد للصفحة كلها. لو الأصفار اختفت
    /// أو عدد المراحل اختلف، كل قسيمة هتبقى بطول مختلف والمقص هيبقى شغل
    /// يدوي لكل واحدة.
    ///
    /// الأرقام كلها بتيجي من <see cref="PayrollService"/> — مفيش أي حساب
    /// هنا، عشان القسيمة اللي في إيد العامل تقول نفس اللي كشف الأجور
    /// بيقوله بالحرف.
    /// </summary>
    public class PayslipStripExcelService
    {
        /// <summary>عدد القسايم في الورقة — صفين × 4 أعمدة</summary>
        public const int SlipsPerPage = 8;

        /// <summary>عدد القسايم في الصف الواحد — القص الرأسي بينهم</summary>
        private const int SlipsPerRow = 4;

        /// <summary>
        /// أقصى عدد مراحل بتتكتب في القسيمة. اللي بيزيد بيتلمّ في سطر
        /// "ومراحل تانية" — عامل اشتغل على مراحل كتير قسيمته هتبقى عمود
        /// أرقام مش ورقة يقراها. اتقلّلت من 6 لـ3 عشان صفين قسايم يدخلوا
        /// في ارتفاع الورقة (شوف تعليق الكلاس).
        /// </summary>
        private const int MaxBreakdownLines = 3;

        // العرض متحسوب عشان 4 قسايم يدخلوا A4 بالعرض بمقاس 100%:
        // (23 + 14) × 4 + 3 فواصل ≈ 151 وحدة ≈ 28 سم من أصل 28.7 متاحة
        private const double LabelWidth = 23;
        private const double ValueWidth = 14;
        private const double GapWidth = 1;

        // ارتفاعات مقلّلة عن نسخة الصف الواحد (كانت 16/32/20/6/20/19/34)
        // عشان صفين قسايم يدخلوا في ارتفاع A4 بالعرض من غير ما FitToPages
        // يصغّر بنسبة كبيرة قوي. القيم هنا بتتلاقى في RowHeights تحت.
        private const double TitleRowHeight = 13;
        private const double NameRowHeight = 24;
        private const double PeriodRowHeight = 15;
        private const double GapRowHeight = 4;
        private const double SectionRowHeight = 15;
        private const double LineRowHeight = 14;
        private const double NetRowHeight = 26;

        /// <summary>صف فاصل واحد بين صف القسايم العلوي والسفلي — بيبقى خط القص الأفقي</summary>
        private const int RowGap = 1;

        // أبيض وأسود: أسود صريح للنص، ورمادي فاتح للتظليل اللي بيفضل
        // يتشاف بعد الطباعة
        private static readonly XLColor Ink = XLColor.Black;
        private static readonly XLColor HeaderFill = XLColor.Black;
        private static readonly XLColor NetFill = XLColor.FromHtml("#D9D9D9");
        private static readonly XLColor SectionFill = XLColor.FromHtml("#EDEDED");

        private const string MoneyFormat = "#,##0 \"ج\"";
        private const string PiecesFormat = "#,##0";

        // اليوميات بتيجي 6.5 و4 و2.6 — تنسيق زي "0.##" بيطبع "4." بنقطة
        // زايدة في الآخر (النقطة حرف حرفي في تنسيق إكسل)، و"0.0" بيطبع
        // "4.0". General بيكتب 6.5 و4 زي ما هما، والأرقام هنا مبتوصلش
        // للألوف فمش محتاجة فاصلة.
        private const string DaysFormat = "General";

        /// <summary>القسيمة الافتراضية: كل البنود ظاهرة — نفس القسيمة اللي كانت موجودة قبل ما البنود تبقى اختيارية</summary>
        public static readonly IReadOnlySet<PayslipStripField> AllFields =
            Enum.GetValues<PayslipStripField>().ToHashSet();

        public void Export(
            PeriodPayrollDto payroll, string filePath, ReportExportOptions? options = null,
            IReadOnlySet<PayslipStripField>? fields = null)
        {
            if (payroll.Workers.Count == 0)
                throw new InvalidOperationException("مفيش عمال في المدة دي لطباعة قسايمهم");

            options ??= new ReportExportOptions();
            fields ??= AllFields;

            using var workbook = new XLWorkbook();

            // بترتيب المدير المخصص: الترتيب ده اللي بيخلي التوزيع سهل —
            // بتدوّر على القسيمة في الورق زي ما بتدوّر على العامل في
            // أي كشف تاني في البرنامج، مش أبجديًا
            var workers = payroll.Workers.OrderBy(w => w.SortOrder).ToList();

            // **عدد سطور المراحل واحد في كل القسايم.** العامل اللي اشتغل
            // على مرحلة واحدة بياخد نفس المساحة بتاعة اللي اشتغل على
            // أربعة، والناقص بيفضل فاضي — وده اللي بيخلي خط القص مستقيم.
            // فاضل صحيح حتى لو StageBreakdown مش ظاهر — ساعتها مش بيتستخدم.
            var breakdownLines = Math.Min(
                MaxBreakdownLines,
                Math.Max(1, workers.Max(w => w.StageBreakdown.Count)));

            var pages = (int)Math.Ceiling(workers.Count / (double)SlipsPerPage);

            for (var page = 0; page < pages; page++)
            {
                var slice = workers.Skip(page * SlipsPerPage).Take(SlipsPerPage).ToList();
                WritePage(workbook, slice, payroll, options, page + 1, pages, breakdownLines, fields);
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
            int breakdownLines,
            IReadOnlySet<PayslipStripField> fields)
        {
            var sheet = workbook.Worksheets.Add(
                pageCount == 1 ? "قسايم الأجر" : $"قسايم {pageNumber}");

            sheet.RightToLeft = true;
            sheet.Style.Font.SetFontName("Arial").Font.SetFontColor(Ink);

            for (var slot = 0; slot < SlipsPerRow; slot++)
            {
                var first = SlotFirstColumn(slot);
                sheet.Column(first).Width = LabelWidth;
                sheet.Column(first + 1).Width = ValueWidth;

                if (slot < SlipsPerRow - 1)
                    sheet.Column(first + 2).Width = GapWidth;
            }

            // **آخر سطر بيتقاس من اللي اتكتب فعلًا، مش من معادلة تانية.**
            // كان فيه معادلة منفصلة (18 + عدد المراحل) بتحسب نفس الرقم
            // من برّه، وكانت غلط بخمس سطور — فالإطار وخط القص كانوا
            // بيقفوا عند "الحساب" وسايبين أجر اليوميات والحوافز والسلف
            // و**الصافي المستحق** برّه الإطار. أي سطر يتزوّد في القسيمة
            // كان هيكسّرها تاني، فالمعادلة اتشالت من أصلها.
            //
            // **صفين قسايم فوق بعض**: كل قسايم نفس الصف بتاخد نفس عدد
            // السطور بالظبط (breakdownLines مشتركة على مستوى الملف كله،
            // مش الصفحة)، فطول الصف العلوي (topHeight) بيبقى واحد سواء
            // كتبنا فيه 1 أو 4 قسايم — وده اللي بيسمح للصف السفلي يبدأ
            // من نفس النقطة (topHeight + RowGap) لأي عدد عمال.
            var topWorkers = workers.Take(SlipsPerRow).ToList();
            var bottomWorkers = workers.Skip(SlipsPerRow).ToList();

            var topHeight = 0;
            for (var slot = 0; slot < topWorkers.Count; slot++)
                topHeight = Math.Max(
                    topHeight,
                    WriteSlip(sheet, topWorkers[slot], payroll, options, slot, 0, breakdownLines, fields));

            if (bottomWorkers.Count > 0)
                sheet.Row(topHeight + 1).Height = GapRowHeight;

            var bottomOffset = topHeight + RowGap;
            var lastRow = topHeight;
            for (var slot = 0; slot < bottomWorkers.Count; slot++)
                lastRow = Math.Max(
                    lastRow,
                    WriteSlip(sheet, bottomWorkers[slot], payroll, options, slot, bottomOffset, breakdownLines, fields));

            // إطار كل قسيمة — حدود الورقة اللي هتتقص
            for (var slot = 0; slot < topWorkers.Count; slot++)
            {
                var col = SlotFirstColumn(slot);
                sheet.Range(1, col, topHeight, col + 1).Style
                    .Border.SetOutsideBorder(XLBorderStyleValues.Medium)
                    .Border.SetOutsideBorderColor(Ink);
            }
            for (var slot = 0; slot < bottomWorkers.Count; slot++)
            {
                var col = SlotFirstColumn(slot);
                sheet.Range(bottomOffset + 1, col, lastRow, col + 1).Style
                    .Border.SetOutsideBorder(XLBorderStyleValues.Medium)
                    .Border.SetOutsideBorderColor(Ink);
            }

            // الفاصل الرأسي بين القسايم = خط قص. منقّط عشان يبان إنه خط
            // قص مش حد جدول، وأسود عشان يفضل يتشاف بعد الطباعة
            for (var slot = 0; slot < SlipsPerRow - 1; slot++)
            {
                var gap = SlotFirstColumn(slot) + 2;
                sheet.Range(1, gap, lastRow, gap).Style
                    .Border.SetLeftBorder(XLBorderStyleValues.Dashed)
                    .Border.SetLeftBorderColor(Ink);
            }

            // الفاصل الأفقي بين الصفين = خط قص تاني — بس لو فيه صف تاني فعلاً
            if (bottomWorkers.Count > 0)
            {
                var lastCol = SlotFirstColumn(SlipsPerRow - 1) + 1;
                sheet.Range(topHeight + 1, 1, topHeight + 1, lastCol).Style
                    .Border.SetTopBorder(XLBorderStyleValues.Dashed)
                    .Border.SetTopBorderColor(Ink);
            }

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.FitToPages(1, 1);
            sheet.PageSetup.Margins.Top = 0.3;
            sheet.PageSetup.Margins.Bottom = 0.3;
            sheet.PageSetup.Margins.Left = 0.2;
            sheet.PageSetup.Margins.Right = 0.2;
            sheet.PageSetup.CenterHorizontally = true;
            sheet.PageSetup.CenterVertically = true;
        }

        /// <summary>أول عمود للقسيمة رقم كذا (عمودين + فاصل)</summary>
        private static int SlotFirstColumn(int slot) => slot * 3 + 1;

        /// <summary>
        /// بيكتب قسيمة واحدة وبيرجّع آخر سطر كتبه. القسايم كلها بتنتهي
        /// عند نفس السطر لأن عدد سطور المراحل واحد فيهم كلهم — وده شرط
        /// خط القص المستقيم.
        /// </summary>
        private int WriteSlip(
            IXLWorksheet sheet,
            WorkerPayrollDto worker,
            PeriodPayrollDto payroll,
            ReportExportOptions options,
            int slot,
            int rowOffset,
            int breakdownLines,
            IReadOnlySet<PayslipStripField> fields)
        {
            var col = SlotFirstColumn(slot);
            var value = col + 1;
            var row = 1 + rowOffset;

            // ---------- اسم المصنع ----------
            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value = options.FactoryName ?? "";
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(8)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Row(row).Height = TitleRowHeight;
            row++;

            // ---------- اسم العامل: أسود مصمت بنص أبيض ----------
            // أقوى تباين ممكن على طابعة أبيض وأسود، والاسم هو أهم حاجة
            // في الورقة لأنه اللي بيحدد القسيمة دي بتاعة مين
            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value = worker.WorkerName;
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(HeaderFill)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Row(row).Height = NameRowHeight;
            row++;

            // ---------- الفترة والنوع ----------
            sheet.Range(row, col, row, value).Merge();
            sheet.Cell(row, col).Value =
                $"{payroll.From:yyyy/MM/dd}  إلى  {payroll.To:yyyy/MM/dd}";
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(8)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Border.SetBottomBorder(XLBorderStyleValues.Medium)
                .Border.SetBottomBorderColor(Ink);
            sheet.Row(row).Height = PeriodRowHeight;
            row = Gap(sheet, row + 1);

            // ---------- الشغل ----------
            // كل قسم بس بيظهر لو بند واحد فيه على الأقل مُعلّم — قسم
            // بعنوان وبلا سطور تحته مالوش معنى
            if (fields.Contains(PayslipStripField.DailyWageRate) ||
                fields.Contains(PayslipStripField.ProducedWorkdays) ||
                fields.Contains(PayslipStripField.DaysWorked))
            {
                row = Section(sheet, col, value, row, "الشغل");
                if (fields.Contains(PayslipStripField.DailyWageRate))
                    row = Line(sheet, col, value, row, "سعر اليومية", worker.DailyWageEgp, MoneyFormat);
                if (fields.Contains(PayslipStripField.ProducedWorkdays))
                    row = Line(sheet, col, value, row, "يوميات منتجة", worker.ProducedWorkdays, DaysFormat);
                if (fields.Contains(PayslipStripField.DaysWorked))
                    row = Line(sheet, col, value, row, "أيام فيها شغل", worker.DaysWorked, PiecesFormat);
                row = Gap(sheet, row);
            }

            // ---------- اشتغل على إيه ----------
            if (fields.Contains(PayslipStripField.StageBreakdown) ||
                fields.Contains(PayslipStripField.TotalPieces))
            {
                row = Section(sheet, col, value, row, "اشتغل على");
                if (fields.Contains(PayslipStripField.StageBreakdown))
                    row = WriteBreakdown(sheet, col, value, row, worker, breakdownLines);
                if (fields.Contains(PayslipStripField.TotalPieces))
                    row = Line(sheet, col, value, row, "إجمالي القطع", worker.TotalPieces, PiecesFormat, strong: true);
                row = Gap(sheet, row);
            }

            // ---------- الخصومات ----------
            if (fields.Contains(PayslipStripField.AbsenceDeduction) ||
                fields.Contains(PayslipStripField.PenaltyDeduction) ||
                fields.Contains(PayslipStripField.NetWorkdays))
            {
                row = Section(sheet, col, value, row, "الخصومات");
                if (fields.Contains(PayslipStripField.AbsenceDeduction))
                    row = Line(sheet, col, value, row, "خصم غياب", worker.AbsenceDeduction, DaysFormat);
                if (fields.Contains(PayslipStripField.PenaltyDeduction))
                    row = Line(sheet, col, value, row, "خصم جزاءات", worker.PenaltyDeduction, DaysFormat);
                if (fields.Contains(PayslipStripField.NetWorkdays))
                    row = Line(sheet, col, value, row, "صافي اليوميات", worker.NetWorkdays, DaysFormat, strong: true);
                row = Gap(sheet, row);
            }

            // ---------- الحساب ----------
            if (fields.Contains(PayslipStripField.WorkdaysWageEgp) ||
                fields.Contains(PayslipStripField.Bonus) ||
                fields.Contains(PayslipStripField.Advance))
            {
                row = Section(sheet, col, value, row, "الحساب");
                if (fields.Contains(PayslipStripField.WorkdaysWageEgp))
                    row = Line(sheet, col, value, row, "أجر اليوميات", worker.WorkdaysWageEgp, MoneyFormat);
                if (fields.Contains(PayslipStripField.Bonus))
                    row = Line(sheet, col, value, row, "حوافز", worker.BonusEgp, MoneyFormat);
                if (fields.Contains(PayslipStripField.Advance))
                    row = Line(sheet, col, value, row, "سلف", worker.AdvanceEgp, MoneyFormat);
                row = Gap(sheet, row);
            }

            // ---------- الصافي المستحق ----------
            sheet.Cell(row, col).Value = "الصافي المستحق";
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(11)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            sheet.Cell(row, value).Value = worker.NetWageEgp;
            sheet.Cell(row, value).Style
                .Font.SetBold().Font.SetFontSize(13)
                .NumberFormat.SetFormat(MoneyFormat);
            sheet.Cell(row, value).Style
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            sheet.Range(row, col, row, value).Style
                .Fill.SetBackgroundColor(NetFill)
                .Border.SetTopBorder(XLBorderStyleValues.Thick)
                .Border.SetBottomBorder(XLBorderStyleValues.Thick)
                .Border.SetTopBorderColor(Ink)
                .Border.SetBottomBorderColor(Ink);
            sheet.Row(row).Height = NetRowHeight;

            return row;
        }

        /// <summary>
        /// سطر فاصل بين الأقسام. رفيع مقصود: بالارتفاع العادي القسيمة
        /// بتبقى مفكّكة وأربع فجوات بتاكل مساحة كبيرة من طول الورقة.
        /// </summary>
        private static int Gap(IXLWorksheet sheet, int row)
        {
            sheet.Row(row).Height = GapRowHeight;
            return row + 1;
        }

        /// <summary>
        /// عنوان قسم: خلفية رمادية فاتحة وخط عريض على عرض القسيمة.
        /// الرمادي الفاتح بيفضل يتشاف بعد الطباعة، والدهبي كان بيختفي.
        /// </summary>
        private static int Section(IXLWorksheet sheet, int col, int valueCol, int row, string title)
        {
            sheet.Range(row, col, row, valueCol).Merge();
            sheet.Cell(row, col).Value = title;
            sheet.Cell(row, col).Style
                .Font.SetBold().Font.SetFontSize(9)
                .Fill.SetBackgroundColor(SectionFill)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Border.SetTopBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetTopBorderColor(Ink)
                .Border.SetBottomBorderColor(Ink);
            sheet.Row(row).Height = SectionRowHeight;
            return row + 1;
        }

        /// <summary>
        /// سطر "بيان: رقم" — بيتكتب حتى لو الرقم صفر.
        ///
        /// **الرقم بيتكتب رقم مش نص**، والوحدة من تنسيق الخلية، ومحاذاته
        /// لليمين — فالأرقام بتصطف تحت بعضها في عمود مستقيم. لما كانت
        /// نصوص ("200 ج" و"0 ج") كانت بتطلع مبعثرة.
        /// </summary>
        private static int Line(
            IXLWorksheet sheet, int col, int valueCol, int row,
            string label, decimal value, string format, bool strong = false)
        {
            sheet.Cell(row, col).Value = label;
            sheet.Cell(row, valueCol).Value = value;
            sheet.Cell(row, valueCol).Style.NumberFormat.SetFormat(format);

            StyleLine(sheet, col, valueCol, row, strong);
            return row + 1;
        }

        /// <summary>سطر نصي (اسم منتج ومرحلة) — نفس شكل السطر الرقمي</summary>
        private static int TextLine(
            IXLWorksheet sheet, int col, int valueCol, int row,
            string label, int? pieces)
        {
            sheet.Cell(row, col).Value = label;

            if (pieces is { } count)
            {
                sheet.Cell(row, valueCol).Value = count;
                sheet.Cell(row, valueCol).Style.NumberFormat.SetFormat(PiecesFormat);
            }

            StyleLine(sheet, col, valueCol, row, strong: false);

            // "كبش الماني — الكبشه كاملة" أطول من عرض العمود، والباقي
            // بيتقص لأن الخانة اللي جنبه مليانة. التصغير التلقائي
            // بيخلّيه يبان كامل بدل ما نص اسم المنتج يضيع.
            sheet.Cell(row, col).Style.Alignment.SetShrinkToFit(true);
            return row + 1;
        }

        private static void StyleLine(IXLWorksheet sheet, int col, int valueCol, int row, bool strong)
        {
            var size = strong ? 10 : 9;

            sheet.Cell(row, col).Style
                .Font.SetFontSize(size).Font.SetBold(strong)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            sheet.Cell(row, valueCol).Style
                .Font.SetFontSize(size).Font.SetBold(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            sheet.Range(row, col, row, valueCol).Style
                .Border.SetBottomBorder(XLBorderStyleValues.Hair)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#808080"));

            sheet.Row(row).Height = LineRowHeight;
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
            // العامل بالساعة مالوش مراحل أصلًا، فالقسم كان بيطلع بلوك
            // فاضي جنب قسايم مليانة — بيقول ليه بدل ما يبان ناقص
            if (worker.StageBreakdown.Count == 0)
            {
                row = TextLine(sheet, col, valueCol, row,
                    worker.IsHourly ? "شغل بالساعة" : "مفيش إنتاج مسجّل", null);

                for (var i = 1; i < lines; i++)
                    row = TextLine(sheet, col, valueCol, row, "", null);

                return row;
            }

            var shown = worker.StageBreakdown.Take(lines).ToList();
            var hidden = worker.StageBreakdown.Count - shown.Count;

            // آخر سطر متاح بيروح لـ"ومراحل تانية" لو فيه مراحل مخفية،
            // عشان الرقم الناقص ميضيعش من غير ما العامل ياخد باله
            var listed = hidden > 0 ? shown.Take(lines - 1).ToList() : shown;

            foreach (var stage in listed)
                row = TextLine(sheet, col, valueCol, row, stage.Display, stage.Pieces);

            if (hidden > 0)
            {
                var rest = worker.StageBreakdown.Skip(listed.Count).Sum(s => s.Pieces);
                row = TextLine(sheet, col, valueCol, row,
                    $"و{worker.StageBreakdown.Count - listed.Count} مراحل تانية", rest);
            }

            // السطور الفاضية بتحجز مكانها عشان القسايم تفضل بنفس الطول
            for (var i = listed.Count + (hidden > 0 ? 1 : 0); i < lines; i++)
                row = TextLine(sheet, col, valueCol, row, "", null);

            return row;
        }
    }
}
