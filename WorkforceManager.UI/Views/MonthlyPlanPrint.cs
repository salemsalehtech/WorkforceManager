using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// طباعة الخطة الشهرية — FlowDocument بنفس ترتيب شيت المصنع (عيلة → منتجاتها
    /// → إجمالي) وخانة توقيع تحت. **PDF من غير مكتبة**: نافذة الطباعة بتاعة
    /// ويندوز فيها "Microsoft Print to PDF"، فنفس الزرار بيطبع ورق أو PDF.
    /// </summary>
    public static class MonthlyPlanPrint
    {
        private static readonly string[] Headers =
            { "المنتج", "المخطط", "المحقق", "Ach %", "نسبة المحقق", "المطلوب يوميًا", "التوقّع" };

        public static void Print(IReadOnlyList<MonthlyPlanTrackingDto> tracking, string title, string periodText)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            var document = Build(tracking, title, periodText);
            document.PageWidth = dialog.PrintableAreaWidth;
            document.PageHeight = dialog.PrintableAreaHeight;
            document.ColumnWidth = dialog.PrintableAreaWidth; // عمود واحد بعرض الصفحة، مش عمودين جرنال
            document.PagePadding = new Thickness(40);

            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
        }

        public static FlowDocument Build(IReadOnlyList<MonthlyPlanTrackingDto> tracking, string title, string periodText)
        {
            var font = Application.Current.TryFindResource("AppFont") as FontFamily ?? new FontFamily("Segoe UI");
            var document = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = font, FontSize = 11 };

            document.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0) });
            document.Blocks.Add(new Paragraph(new Run(periodText)) { Foreground = Brushes.DimGray, TextAlignment = TextAlignment.Center });

            var inPlan = tracking.Where(p => !p.IsOutsidePlan).ToList();
            if (inPlan.Count > 0)
            {
                var planned = inPlan.Sum(p => p.PlannedQuantity);
                var achieved = inPlan.Sum(p => p.EffectiveAchieved);
                var summary = $"خطة الشهر {planned:N0} · المحقق {achieved:N0}" +
                              (planned > 0 ? $" · الإنجاز {(decimal)achieved / planned * 100:0}%" : "") +
                              $" · أيام الشغل الباقية {inPlan[0].RemainingWorkdays} من {inPlan[0].TotalWorkdays}";
                document.Blocks.Add(new Paragraph(new Run(summary)) { FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
            }

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
            for (var i = 1; i < Headers.Length; i++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            group.Rows.Add(Row(Headers, bold: true, background: new SolidColorBrush(Color.FromRgb(0xE2, 0xEF, 0xDA))));

            foreach (var material in inPlan.GroupBy(p => p.Material)
                         .OrderBy(g => g.Key switch { Material.Copper => 0, Material.Zamak => 1, _ => 2 }))
            {
                var materialName = material.Key switch { Material.Copper => "النحاس", Material.Zamak => "الزاما", _ => "غير محدد" };
                foreach (var family in material.GroupBy(p => p.FamilyName ?? MonthlyPlanStatisticsFilter.NoFamily).OrderBy(g => g.Key))
                {
                    group.Rows.Add(SpanRow(family.Key, new SolidColorBrush(Color.FromRgb(0xD6, 0xDC, 0xE4))));
                    foreach (var p in family.OrderBy(p => p.ProductName))
                        group.Rows.Add(Row(Cells(p.ProductName, p.PlannedQuantity, p.EffectiveAchieved, p.AchievedPercent,
                            p.RequiredDailyOutput, p.ForecastEndOfMonth)));
                    group.Rows.Add(TotalRow($"إجمالي {family.Key}", family.ToList(), new SolidColorBrush(Color.FromRgb(0xF1, 0xEC, 0xDF))));
                }
                group.Rows.Add(TotalRow($"إجمالي محقق {materialName}", material.ToList(), new SolidColorBrush(Color.FromRgb(0xC6, 0xE0, 0xB4))));
            }
            group.Rows.Add(TotalRow("الإجمالي العام", inPlan, new SolidColorBrush(Color.FromRgb(0xF8, 0xCB, 0xAD))));
            document.Blocks.Add(table);

            var outside = tracking.Where(p => p.IsOutsidePlan).ToList();
            if (outside.Count > 0)
                document.Blocks.Add(new Paragraph(new Run("خارج الخطة: " +
                    string.Join("، ", outside.Select(p => $"{p.ProductName} ({p.EffectiveAchieved:N0})")))) { Margin = new Thickness(0, 12, 0, 0) });

            document.Blocks.Add(new Paragraph(new Run("توقيع المسؤول: ____________________        التاريخ: ____ / ____ / ________"))
                { Margin = new Thickness(0, 30, 0, 0) });
            return document;
        }

        private static string[] Cells(string name, int plan, int achieved, decimal? pace, int? required, int? forecast) =>
            new[]
            {
                name, plan.ToString("N0"), achieved.ToString("N0"),
                plan > 0 ? $"{(decimal)achieved / plan * 100:0}%" : "—",
                pace is { } p ? $"{p * 100:0}%" : "—",
                plan > 0 && required is { } r ? r.ToString("N0") : "—",
                forecast?.ToString("N0") ?? "—"
            };

        private static TableRow TotalRow(string label, IReadOnlyList<MonthlyPlanTrackingDto> products, Brush background)
        {
            var plan = products.Sum(p => p.PlannedQuantity);
            var achieved = products.Sum(p => p.EffectiveAchieved);
            var proRated = products.Sum(p => p.ProRatedPlan);
            var remaining = products.Count > 0 ? products[0].RemainingWorkdays : 0;
            var forecast = products.All(p => p.ForecastEndOfMonth is not null) && products.Count > 0
                ? products.Sum(p => p.ForecastEndOfMonth!.Value) : (int?)null;
            return Row(Cells(label, plan, achieved, proRated > 0 ? achieved / proRated : null,
                MonthlyPlanFamilyMath.RequiredDailyOutput(plan, achieved, remaining), forecast), bold: true, background: background);
        }

        private static TableRow Row(IEnumerable<string> cells, bool bold = false, Brush? background = null)
        {
            var row = new TableRow { Background = background };
            var first = true;
            foreach (var text in cells)
            {
                row.Cells.Add(new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
                {
                    Padding = new Thickness(5, 3, 5, 3),
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5),
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = first ? TextAlignment.Right : TextAlignment.Center
                });
                first = false;
            }
            return row;
        }

        private static TableRow SpanRow(string text, Brush background)
        {
            var row = new TableRow { Background = background };
            row.Cells.Add(new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
            {
                ColumnSpan = Headers.Length,
                Padding = new Thickness(5, 3, 5, 3),
                FontWeight = FontWeights.Bold
            });
            return row;
        }
    }
}
