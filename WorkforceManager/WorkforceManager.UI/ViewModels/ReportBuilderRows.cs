using CommunityToolkit.Mvvm.ComponentModel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    // خيارات وصفوف شاشة مُنشئ التقارير.

    public record SubjectOption(ReportSubject Subject, string Display);

    public record GroupingOption(ReportGrouping Grouping, string Display);

    public record PeriodOption(ReportPeriodKind Kind, string Display);

    /// <summary>نوع العامل في الفلترة</summary>
    public record WorkerKindOption(WorkerKindFilter Kind, string Display);

    /// <summary>عمود الترتيب — null = الترتيب الطبيعي للموضوع</summary>
    public record SortOption(string? Key, string Display);

    /// <summary>أعلى كام صف — null = كل الصفوف</summary>
    public record TopNOption(int? Count, string Display);

    /// <summary>مرحلة في قايمة الفلترة — null = كل المراحل</summary>
    public record StageFilterItem(int? Id, string Display);

    /// <summary>
    /// عنصر بيتعلّم في قايمة اختيار متعدد (عمال أو منتجات).
    ///
    /// لازم يكون <see cref="ObservableObject"/> مش record: المستخدم
    /// بيعلّم ويشيل العلامة والشاشة لازم تسمع كل تغيير عشان تعيد بناء
    /// المعاينة وتحدّث عدّاد الفلاتر.
    /// </summary>
    public partial class CheckableItem : ObservableObject
    {
        public CheckableItem(int id, string display)
        {
            Id = id;
            Display = display;
        }

        public int Id { get; }
        public string Display { get; }

        [ObservableProperty]
        private bool _isChecked;
    }

    /// <summary>بند اختياري في قسيمة الأجر المطبوعة — شوف ReportBuilderViewModel.PayslipFieldChoices</summary>
    public partial class PayslipFieldChoice : ObservableObject
    {
        public PayslipFieldChoice(PayslipStripField field, string display)
        {
            Field = field;
            Display = display;
        }

        public PayslipStripField Field { get; }
        public string Display { get; }

        [ObservableProperty]
        private bool _isChecked = true;

        partial void OnIsCheckedChanged(bool value) => Changed?.Invoke();

        /// <summary>بيتنادى مباشرة (مش Command) عشان الحفظ يحصل فورًا مع كل تعليم/إلغاء</summary>
        public event Action? Changed;
    }

    /// <summary>
    /// عمود في محرّر الأعمدة: يظهر ولا لأ، واسمه إيه، وترتيبه.
    ///
    /// <see cref="Key"/> هو الرابط بالمحرك وعمره ما بيتغيّر — الاسم
    /// اللي المستخدم بيكتبه بيتحط في <see cref="Header"/> بس، عشان
    /// إعادة التسمية متكسرش القوالب المحفوظة.
    /// </summary>
    public partial class ColumnChoiceItem : ObservableObject
    {
        public ColumnChoiceItem(
            string key, string defaultHeader, bool visible = true, string? header = null,
            bool canSum = false, bool? sums = null)
        {
            Key = key;
            DefaultHeader = defaultHeader;
            CanSum = canSum;
            DefaultSums = canSum;
            _isVisible = visible;
            _header = header ?? defaultHeader;
            _sums = sums ?? canSum;
        }

        /// <summary>
        /// العمود ده أصلاً ينفع يتجمّع؟ الأعمدة النصية (المرحلة، المنتج)
        /// مالهاش إجمالي، فعلامة الجمع بتختفي عندها بدل ما تبقى موجودة
        /// ومالهاش أي أثر.
        /// </summary>
        public bool CanSum { get; }

        /// <summary>قرار البرنامج الأصلي — عشان زرار "رجّع"</summary>
        public bool DefaultSums { get; }

        [ObservableProperty]
        private bool _sums;

        public string Key { get; }

        /// <summary>الاسم الأصلي — عشان زرار "رجّع الأسماء"</summary>
        public string DefaultHeader { get; }

        [ObservableProperty]
        private bool _isVisible;

        [ObservableProperty]
        private string _header;

        /// <summary>اسم متغيّر عن الأصلي؟ (بيتعرض بعلامة في القايمة)</summary>
        public bool IsRenamed => !string.Equals(Header?.Trim(), DefaultHeader, StringComparison.Ordinal);

        partial void OnHeaderChanged(string value) => OnPropertyChanged(nameof(IsRenamed));

        public ReportColumnChoice ToChoice() => new()
        {
            Key = Key,
            Visible = IsVisible,
            Header = string.IsNullOrWhiteSpace(Header) || Header.Trim() == DefaultHeader
                ? null
                : Header.Trim(),

            // null = سيب قرار البرنامج. بنبعت قيمة بس لما المستخدم
            // يغيّرها فعلاً، فالقوالب القديمة متتأثرش
            Sums = Sums == DefaultSums ? null : Sums
        };
    }

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
                // العمود النصي (زي مستوى تجميع إضافي: المنتج، المرحلة)
                // قيمته في Texts مش في Values. من غير ده كان بيطلع شرطة
                // في كل سطر لأن Values عنده null هناك.
                var text = i < row.Texts.Count ? row.Texts[i] : null;
                if (!string.IsNullOrEmpty(text))
                {
                    cells.Add(text);
                    continue;
                }

                var value = i < row.Values.Count ? row.Values[i] : null;

                // سطر الإجمالي تحت عمود نصي بيفضل فاضي مش شرطة —
                // "إجمالي المرحلة" مالوش معنى
                cells.Add(value is null && columns[i].Kind == ReportValueKind.Text
                    ? ""
                    : Format(value, columns[i].Kind));
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
