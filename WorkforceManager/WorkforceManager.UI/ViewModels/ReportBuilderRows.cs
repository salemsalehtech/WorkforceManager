using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    // خيارات وصفوف شاشة مُنشئ التقارير.

    public record SubjectOption(ReportSubject Subject, string Display);

    public record GroupingOption(ReportGrouping Grouping, string Display);

    public record PeriodOption(ReportPeriodKind Kind, string Display);

    /// <summary>عامل في قايمة الفلترة — null = كل العمال</summary>
    public record WorkerFilterItem(int? Id, string Display);

    /// <summary>منتج في قايمة الفلترة — null = كل المنتجات</summary>
    public record ProductFilterItem(int? Id, string Display);

    /// <summary>
    /// سطر في جدول المعاينة.
    ///
    /// القيم نصوص جاهزة مش أرقام: التنسيق (فاصلة الآلاف، خانتين
    /// عشريتين) بيتحدد من نوع العمود مرة واحدة هنا، فالشبكة في XAML
    /// مش محتاجة تعرف أي حاجة عن الموضوع — وده اللي بيخلي شبكة واحدة
    /// تعرض الستة مواضيع.
    /// </summary>
    public class PreviewRow
    {
        public string Label { get; private init; } = "";
        public IReadOnlyList<string> Cells { get; private init; } = Array.Empty<string>();

        /// <summary>سطر الإجمالي — بيتعرض بخط عريض وخلفية مختلفة</summary>
        public bool IsTotals { get; private init; }

        public static PreviewRow From(ReportRow row, IReadOnlyList<ReportColumn> columns, bool isTotals)
        {
            var cells = new List<string>(columns.Count);

            for (var i = 0; i < columns.Count; i++)
            {
                var value = i < row.Values.Count ? row.Values[i] : null;
                cells.Add(Format(value, columns[i].Kind));
            }

            return new PreviewRow { Label = row.Label, Cells = cells, IsTotals = isTotals };
        }

        /// <summary>
        /// الخانة الفاضية بتتعرض شرطة مش صفر: الصفر رقم، والشرطة معناها
        /// "السؤال ده مالوش إجابة هنا" — زي مجموع عدد العمال.
        /// </summary>
        private static string Format(decimal? value, ReportValueKind kind)
        {
            if (value is null) return "—";

            return kind switch
            {
                ReportValueKind.Money => $"{value.Value:N0}",
                ReportValueKind.Fraction => $"{value.Value:0.##}",
                ReportValueKind.Whole => $"{value.Value:N0}",
                _ => value.Value.ToString("0.##")
            };
        }
    }
}
