using System.Globalization;
using System.Windows.Data;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// بيحوّل عرض حاوية شبكة الكروت (ActualWidth، بيتغيّر مع كل Resize) +
    /// حالة الشريط الجانبي (مطوي/مفتوح) لعرض كارت واحد — بدل تخمين رقم
    /// عرض ثابت بيصح على شاشة ويغلط على تانية.
    ///
    /// **العدد المستهدف مرتبط بحالة الشريط تحديدًا، مش بعرض النافذة
    /// وبس.** أول تصميم كان عدد أعمدة ثابت (5) بحد أدنى لعرض الكارت
    /// (170px) — لو المساحة مش كفاية لـ5 كروت ≥170px كان الكارت بيتثبّت
    /// على 170 والـWrapPanel بيلف الخامس لسطر جديد (4 بس)، حتى لو
    /// المستخدم كبّر النافذة أو طوى الشريط متوقّع عدد أكبر مش أصغر.
    /// المحاولة التانية كانت حساب تلقائي بالكامل من عرض المساحة (زي CSS's
    /// repeat(auto-fill))، لكن طلع سلوكه متغيّر مع حجم النافذة مش بس مع
    /// حالة الشريط — والمطلوب فعليًا قاعدة واضحة: الشريط موجود = 5،
    /// الشريط مطوي = 6، مهما كان حجم النافذة غير كده.
    ///
    /// MultiConvert (الاستخدام الفعلي في WorkersView/ProductsView):
    /// [0] = ActualWidth حاوية الشبكة، [1] = MainWindow.IsSidebarCollapsed.
    /// Convert (الأحادي، احتياطي لأي شبكة تانية مش مربوطة بحالة الشريط):
    /// بيستخدم DefaultMaxColumns أو رقم من ConverterParameter.
    /// </summary>
    public class GridColumnWidthConverter : IValueConverter, IMultiValueConverter
    {
        /// <summary>هامش كل كارت (يمين+تحت، Margin="0,0,10,10" في XAML) — لازم يتوافق مع القيمة الفعلية</summary>
        private const double CardMargin = 10;

        /// <summary>حد أدنى لعرض الكارت — أي حاجة أضيق من كده بتكسر قراءة الاسم/البادجات</summary>
        private const double MinCardWidth = 170;

        /// <summary>أقل عدد أعمدة حتى لو المساحة ضاقت جدًا — تفادي شكل عمود واحد/عمودين غريب</summary>
        private const int MinColumns = 3;

        /// <summary>عدد الأعمدة المستهدف والشريط الجانبي ظاهر</summary>
        private const int ColumnsWithSidebar = 5;

        /// <summary>عدد الأعمدة المستهدف والشريط الجانبي مطوي</summary>
        private const int ColumnsSidebarCollapsed = 6;

        /// <summary>أقصى عدد أعمدة افتراضي لاستخدام Convert الأحادي (بدون معرفة حالة الشريط)</summary>
        private const int DefaultMaxColumns = 6;

        /// <summary>
        /// هامش أمان لكل كارت. من غيره الكروت كانت بتملا السطر بالظبط من غير
        /// ولا بكسل زيادة، وتقريب WPF لعرض كل كارت لأقرب بكسل حقيقي (مع
        /// تكبير الشاشة وUiScale) بيزوّد مجموع السطر كسر بكسل لكل كارت —
        /// فآخر كارت مبيلاقيش مكان والـWrapPanel بيلفّه لسطر جديد: 4 أعمدة
        /// بدل 5 على شاشات معيّنة، وسليم على غيرها حسب التقريب بالصدفة.
        /// </summary>
        private const double RoundingSlack = 1;

        /// <summary>
        /// عدد الأعمدة الفعلي لعرض حاوية معيّن — دالة نقية منفصلة عن حساب
        /// العرض عشان تتختبر لوحدها (شوف GridColumnCountTests).
        /// </summary>
        public static int ColumnsFor(double containerWidth, int maxColumns)
        {
            // كل كارت محتاج عرضه + هامشه اليمين (حتى آخر كارت في السطر —
            // Margin="0,0,10,10" على كل الكروت، والـWrapPanel بيحسبه) + هامش الأمان
            var columnsThatFit = (int)Math.Floor(containerWidth / (MinCardWidth + CardMargin + RoundingSlack));
            return Math.Clamp(columnsThatFit, MinColumns, maxColumns);
        }

        /// <summary>نفس ColumnsFor، بس بحالة الشريط الجانبي مباشرة بدل maxColumns يدوي — أقرب لواجهة MultiConvert الفعلية</summary>
        public static int ColumnsFor(double containerWidth, bool isSidebarCollapsed) =>
            ColumnsFor(containerWidth, isSidebarCollapsed ? ColumnsSidebarCollapsed : ColumnsWithSidebar);

        private static double WidthFor(double containerWidth, int maxColumns)
        {
            if (containerWidth <= 0) return 250d;

            var columns = ColumnsFor(containerWidth, maxColumns);
            var width = containerWidth / columns - CardMargin - RoundingSlack;
            return Math.Max(MinCardWidth, width);
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double containerWidth) return 250d;

            var maxColumns = parameter is string s && int.TryParse(s, out var parsed) && parsed > 0
                ? parsed
                : DefaultMaxColumns;

            return WidthFor(containerWidth, maxColumns);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is not [double containerWidth, bool isSidebarCollapsed]) return 250d;

            var maxColumns = isSidebarCollapsed ? ColumnsSidebarCollapsed : ColumnsWithSidebar;
            return WidthFor(containerWidth, maxColumns);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
