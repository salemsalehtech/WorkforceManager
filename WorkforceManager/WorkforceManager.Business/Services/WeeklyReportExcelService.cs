using ClosedXML.Excel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تصدير كشف الأسبوع لملف Excel حقيقي (xlsx) منسّق وجاهز للطباعة
    /// أو الإرسال — نفس الكشف اللي المدير كان بيعمله يدويًا في الإكسل،
    /// لكن هنا الأرقام محسوبة من النظام والإكسل بقى مجرد مخرج نهائي.
    /// الخدمة بتاخد البيانات جاهزة ومش بتلمس قاعدة البيانات — كل
    /// الحسابات مسؤولية WeeklySummaryService.
    /// </summary>
    public class WeeklyReportExcelService
    {
        // ألوان الكشف (متناسقة مع ألوان البرنامج)
        private static readonly XLColor HeaderColor = XLColor.FromHtml("#1F3864");
        private static readonly XLColor BestRowColor = XLColor.FromHtml("#FFF6D9");
        private static readonly XLColor StripeColor = XLColor.FromHtml("#F2F4F8");

        /// <summary>
        /// يبني ملف الكشف ويحفظه في المسار المطلوب. بيرمي استثناء واضح
        /// لو القائمة فاضية بدل ما يطلع ملف فاضي مضلل.
        /// </summary>
        public void ExportWeeklySummary(IReadOnlyList<WorkerWeeklySummaryDto> summaries, string filePath)
        {
            if (summaries.Count == 0)
                throw new InvalidOperationException("لا توجد بيانات في هذا الأسبوع للتصدير");

            var weekStart = summaries[0].WeekStart;
            var weekEnd = summaries[0].WeekEnd;

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("كشف الأسبوع");
            sheet.RightToLeft = true; // الشيت كله يمين-لشمال زي البرنامج

            // ---------- صف العنوان (مدموج فوق الجدول) ----------
            sheet.Range(1, 1, 1, 10).Merge();
            var title = sheet.Cell(1, 1);
            title.Value = $"كشف أسبوع العمل: من الخميس {weekStart:yyyy/MM/dd} إلى الأربعاء {weekEnd:yyyy/MM/dd}";
            title.Style.Font.SetBold().Font.SetFontSize(14);
            title.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Row(1).Height = 26;

            // ---------- صف العناوين ----------
            // **لازم يفضل مطابق لأعمدة الكشف على الشاشة.** أي عمود بيتشال
            // من هنا أو من هناك لازم يتشال من التاني — ملف بيقول حاجة
            // والشاشة بتقول حاجة تانية بيخلي المستخدم ميثقش في الاتنين.
            string[] headers =
            {
                "الترتيب", "اسم العامل",
                "اليوميات المنتجة", "إجمالي القطع",
                "الغياب", "إجمالي الخصم", "صافي اليوميات",
                "سعر اليومية", "الأجر بالجنيه", "تفاصيل الجزاءات"
            };
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Fill.SetBackgroundColor(HeaderColor);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            sheet.Row(2).Height = 22;

            // ---------- صفوف العمال (مرتبين بالصافي زي ما جايين من الخدمة) ----------
            for (var i = 0; i < summaries.Count; i++)
            {
                var s = summaries[i];
                var row = i + 3; // البيانات بتبدأ من الصف الثالث (بعد العنوان والهيدر)

                sheet.Cell(row, 1).Value = i + 1;
                sheet.Cell(row, 2).Value = s.IsBestWorkerOfWeek ? $"⭐ {s.WorkerName}" : s.WorkerName;
                sheet.Cell(row, 3).Value = s.ProducedWorkdays;
                sheet.Cell(row, 4).Value = s.TotalPieces;
                // الغياب في خانة واحدة بنفس صيغة الشاشة
                var totalAbsence = s.AbsentWithPermissionDays + s.AbsentWithoutPermissionDays;
                sheet.Cell(row, 5).Value = totalAbsence == 0
                    ? "—"
                    : s.AbsentWithPermissionDays == 0
                        ? totalAbsence.ToString()
                        : $"{totalAbsence} (منهم {s.AbsentWithPermissionDays} بإذن)";

                sheet.Cell(row, 6).Value = s.AbsenceDeduction + s.PenaltyDeduction;
                sheet.Cell(row, 7).Value = s.NetWorkdays;
                sheet.Cell(row, 8).Value = s.DailyWageEgp;
                sheet.Cell(row, 9).Value = s.NetWageEgp;
                sheet.Cell(row, 10).Value = string.Join("، ",
                    s.Penalties.Select(p => $"{p.Reason} ({p.DeductionName})"));

                // الصافي بالخط العريض، وبالأحمر لو سالب (عامل عليه خصومات أكتر من إنتاجه)
                var netCell = sheet.Cell(row, 7);
                netCell.Style.Font.SetBold();
                if (s.NetWorkdays < 0)
                    netCell.Style.Font.SetFontColor(XLColor.Red);

                // عمود الأجر بالخط العريض بلون أخضر — أهم رقم في الكشف
                var wageCell = sheet.Cell(row, 9);
                wageCell.Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#0B6E4F"));
                wageCell.Style.NumberFormat.Format = "#,##0 \"ج\"";

                // تظليل: أحسن عامل أصفر فاتح، والصفوف الزوجية رمادي خفيف (سهولة القراءة)
                if (s.IsBestWorkerOfWeek)
                    sheet.Range(row, 1, row, 10).Style.Fill.SetBackgroundColor(BestRowColor);
                else if (i % 2 == 1)
                    sheet.Range(row, 1, row, 10).Style.Fill.SetBackgroundColor(StripeColor);
            }

            // ---------- صف الإجمالي (مجموع الأجور — الأهم للمحاسبة) ----------
            var totalRow = summaries.Count + 3;
            sheet.Cell(totalRow, 2).Value = "الإجمالي";
            sheet.Cell(totalRow, 9).Value = summaries.Sum(s => s.NetWageEgp);
            sheet.Cell(totalRow, 9).Style.NumberFormat.Format = "#,##0 \"ج\"";
            sheet.Range(totalRow, 1, totalRow, 10).Style.Font.SetBold();
            sheet.Range(totalRow, 1, totalRow, 10).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EDF7"));

            // ---------- تنسيق عام ----------
            var lastRow = totalRow;
            var table = sheet.Range(2, 1, lastRow, 10);
            table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            table.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            // الأعمدة الرقمية في النص، والأسماء والجزاءات على اليمين
            sheet.Range(3, 1, lastRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Range(3, 3, lastRow, 9).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            sheet.SheetView.FreezeRows(2); // العنوان والهيدر ثابتين مع التمرير
            sheet.Columns().AdjustToContents();
            sheet.Column(2).Width = Math.Max(sheet.Column(2).Width, 28);  // عمود الاسم مش بيتزنق
            sheet.Column(10).Width = Math.Max(sheet.Column(10).Width, 30); // عمود الجزاءات كذلك

            workbook.SaveAs(filePath);
        }

        /// <summary>
        /// تصدير كشف أجور فترة مخصصة (شهري) لملف Excel: كل عامل بصافي
        /// يومياته وسعره وأجره النهائي، مع صف إجمالي الأجور — الملف اللي
        /// المدير بيطبعه أو يبعته لحساب الرواتب.
        /// </summary>
        public void ExportPeriodPayroll(PeriodPayrollDto payroll, string filePath)
        {
            if (payroll.Workers.Count == 0)
                throw new InvalidOperationException("لا توجد بيانات في هذه الفترة للتصدير");

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("كشف الأجور");
            sheet.RightToLeft = true;

            const int cols = 9;
            sheet.Range(1, 1, 1, cols).Merge();
            var title = sheet.Cell(1, 1);
            title.Value = $"كشف أجور الفترة: من {payroll.From:yyyy/MM/dd} إلى {payroll.To:yyyy/MM/dd}";
            title.Style.Font.SetBold().Font.SetFontSize(14);
            title.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Row(1).Height = 26;

            string[] headers =
            {
                "الترتيب", "اسم العامل", "النوع",
                "أيام العمل", "صافي اليوميات", "سعر اليومية", "حافز", "سلفة", "الأجر بالجنيه"
            };
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(2, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Fill.SetBackgroundColor(HeaderColor);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            sheet.Row(2).Height = 22;

            for (var i = 0; i < payroll.Workers.Count; i++)
            {
                var w = payroll.Workers[i];
                var row = i + 3;
                sheet.Cell(row, 1).Value = i + 1;
                sheet.Cell(row, 2).Value = w.WorkerName;
                sheet.Cell(row, 3).Value = w.IsHourly ? "بالساعة" : "إنتاج";
                sheet.Cell(row, 4).Value = w.DaysWorked;
                sheet.Cell(row, 5).Value = w.NetWorkdays;
                sheet.Cell(row, 6).Value = w.DailyWageEgp;
                sheet.Cell(row, 7).Value = w.BonusEgp;
                sheet.Cell(row, 8).Value = w.AdvanceEgp;
                sheet.Cell(row, 9).Value = w.NetWageEgp;

                if (w.BonusEgp > 0) sheet.Cell(row, 7).Style.Font.SetFontColor(XLColor.FromHtml("#0B6E4F"));
                if (w.AdvanceEgp > 0) sheet.Cell(row, 8).Style.Font.SetFontColor(XLColor.FromHtml("#B00020"));

                var wageCell = sheet.Cell(row, 9);
                wageCell.Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#0B6E4F"));
                wageCell.Style.NumberFormat.Format = "#,##0 \"ج\"";

                if (i % 2 == 1)
                    sheet.Range(row, 1, row, cols).Style.Fill.SetBackgroundColor(StripeColor);
            }

            // صف الإجمالي
            var totalRow = payroll.Workers.Count + 3;
            sheet.Cell(totalRow, 2).Value = "الإجمالي";
            sheet.Cell(totalRow, 7).Value = payroll.Workers.Sum(w => w.BonusEgp);
            sheet.Cell(totalRow, 8).Value = payroll.Workers.Sum(w => w.AdvanceEgp);
            sheet.Cell(totalRow, 9).Value = payroll.TotalWageEgp;
            sheet.Cell(totalRow, 9).Style.NumberFormat.Format = "#,##0 \"ج\"";
            sheet.Range(totalRow, 1, totalRow, cols).Style.Font.SetBold();
            sheet.Range(totalRow, 1, totalRow, cols).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EDF7"));

            var table = sheet.Range(2, 1, totalRow, cols);
            table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            table.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Range(3, 3, totalRow, cols).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            sheet.SheetView.FreezeRows(2);
            sheet.Columns().AdjustToContents();
            sheet.Column(2).Width = Math.Max(sheet.Column(2).Width, 28);

            workbook.SaveAs(filePath);
        }

        /// <summary>تصدير التقرير العام للإنتاج (ملخص + بالمنتج/المرحلة + بالعامل) لملف Excel</summary>
        public void ExportGeneralReport(GeneralProductionReportDto report, string filePath)
        {
            using var workbook = new XLWorkbook();

            // ---- ورقة: بالمنتج والمرحلة ----
            var s1 = workbook.Worksheets.Add("بالمنتج والمرحلة");
            s1.RightToLeft = true;
            s1.Range(1, 1, 1, 4).Merge();
            s1.Cell(1, 1).Value = $"تقرير الإنتاج العام: من {report.From:yyyy/MM/dd} إلى {report.To:yyyy/MM/dd}";
            s1.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            s1.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            // ملخص إجمالي
            s1.Cell(2, 1).Value =
                $"القطع المكتملة: {report.TotalCompletedPieces:N0}   |   اليوميات: {report.TotalWorkdays:0.##}   |   العمال: {report.WorkersCount}   |   أيام الإنتاج: {report.ProductionDays}";
            s1.Range(2, 1, 2, 4).Merge();
            s1.Cell(2, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#1F3864"));

            WriteHeaders(s1, 3, "المنتج", "المرحلة", "القطع", "اليوميات");
            var r = 4;
            foreach (var row in report.ByProductStage)
            {
                s1.Cell(r, 1).Value = row.ProductName;
                s1.Cell(r, 2).Value = row.IsLastStage ? $"{row.StageName} (مكتمل)" : row.StageName;
                s1.Cell(r, 3).Value = row.Pieces;
                s1.Cell(r, 4).Value = row.Workdays;
                if ((r - 4) % 2 == 1) s1.Range(r, 1, r, 4).Style.Fill.SetBackgroundColor(StripeColor);
                r++;
            }
            FinishSheet(s1, 3, r - 1, 4);

            // ---- ورقة: بالعامل ----
            var s2 = workbook.Worksheets.Add("بالعامل");
            s2.RightToLeft = true;
            WriteHeaders(s2, 1, "الترتيب", "اسم العامل", "النوع", "القطع", "اليوميات");
            r = 2;
            for (var i = 0; i < report.ByWorker.Count; i++)
            {
                var w = report.ByWorker[i];
                s2.Cell(r, 1).Value = i + 1;
                s2.Cell(r, 2).Value = w.WorkerName;
                s2.Cell(r, 3).Value = w.IsHourly ? "بالساعة" : "إنتاج";
                s2.Cell(r, 4).Value = w.TotalPieces;
                s2.Cell(r, 5).Value = w.TotalWorkdays;
                if (i % 2 == 1) s2.Range(r, 1, r, 5).Style.Fill.SetBackgroundColor(StripeColor);
                r++;
            }
            FinishSheet(s2, 1, r - 1, 5);

            workbook.SaveAs(filePath);
        }

        /// <summary>تصدير تقرير عامل معين (الإنتاج + الحضور + الأجر) لملف Excel</summary>
        public void ExportWorkerReport(WorkerProductionReportDto report, string filePath)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("تقرير العامل");
            sheet.RightToLeft = true;

            sheet.Range(1, 1, 1, 4).Merge();
            sheet.Cell(1, 1).Value = $"تقرير العامل: {report.WorkerName}";
            sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            sheet.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Range(2, 1, 2, 4).Merge();
            sheet.Cell(2, 1).Value = $"الفترة: من {report.From:yyyy/MM/dd} إلى {report.To:yyyy/MM/dd}   |   النوع: {report.TypeText}";
            sheet.Cell(2, 1).Style.Font.SetBold();

            // ملخص
            var row = 4;
            void Info(string label, string val)
            {
                sheet.Cell(row, 1).Value = label;
                sheet.Cell(row, 1).Style.Font.SetBold();
                sheet.Cell(row, 2).Value = val;
                row++;
            }
            Info("إجمالي القطع", $"{report.TotalPieces:N0}");
            Info("اليوميات المنتجة", $"{report.ProducedWorkdays:0.##}");
            Info("أيام حضور", $"{report.PresentDays}");
            Info("غياب بإذن", $"{report.AbsentWithPermissionDays}");
            Info("غياب بدون إذن", $"{report.AbsentWithoutPermissionDays}  (خصم {report.AbsenceDeduction})");
            Info("خصم الجزاءات", $"{report.PenaltyDeduction}");
            Info("صافي اليوميات", $"{report.NetWorkdays:0.##}");
            if (report.DailyWageEgp > 0)
            {
                Info("سعر اليومية", $"{report.DailyWageEgp:N0} ج");
                Info("أجر اليوميات", $"{report.WorkdaysWageEgp:N0} ج");
            }
            if (report.BonusEgp > 0) Info("الحوافز", $"+ {report.BonusEgp:N0} ج");
            if (report.AdvanceEgp > 0) Info("السلف", $"− {report.AdvanceEgp:N0} ج");
            Info("الأجر النهائي", $"{report.NetWageEgp:N0} ج");

            // تفصيل الإنتاج بالمنتج/المرحلة
            row += 1;
            WriteHeaders(sheet, row, "المنتج", "المرحلة", "القطع", "اليوميات");
            var start = row; row++;
            foreach (var ps in report.ByProductStage)
            {
                sheet.Cell(row, 1).Value = ps.ProductName;
                sheet.Cell(row, 2).Value = ps.StageName;
                sheet.Cell(row, 3).Value = ps.Pieces;
                sheet.Cell(row, 4).Value = ps.Workdays;
                row++;
            }
            FinishSheet(sheet, start, row - 1, 4);

            // تفصيل باليوم
            row += 1;
            WriteHeaders(sheet, row, "التاريخ", "القطع", "اليوميات", "التفصيل");
            start = row; row++;
            foreach (var d in report.ByDay)
            {
                sheet.Cell(row, 1).Value = d.Date.ToString("yyyy/MM/dd");
                sheet.Cell(row, 2).Value = d.Pieces;
                sheet.Cell(row, 3).Value = d.Workdays;
                sheet.Cell(row, 4).Value = d.Detail;
                row++;
            }
            FinishSheet(sheet, start, row - 1, 4);

            sheet.Columns().AdjustToContents();
            sheet.Column(4).Width = Math.Max(sheet.Column(4).Width, 30);
            workbook.SaveAs(filePath);
        }

        // ------- مساعدات تنسيق مشتركة -------

        private static void WriteHeaders(ClosedXML.Excel.IXLWorksheet sheet, int rowIndex, params string[] headers)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(rowIndex, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Fill.SetBackgroundColor(HeaderColor);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
        }

        private static void FinishSheet(ClosedXML.Excel.IXLWorksheet sheet, int headerRow, int lastRow, int cols)
        {
            if (lastRow < headerRow) return;
            var table = sheet.Range(headerRow, 1, lastRow, cols);
            table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
            table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            table.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            sheet.Columns().AdjustToContents();
        }
    }
}
