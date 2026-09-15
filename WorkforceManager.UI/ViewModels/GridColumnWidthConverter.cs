using System.Globalization;
using System.Windows.Data;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// بيحوّل عرض حاوية شبكة الكروت (ActualWidth، بيتغيّر مع كل Resize) لعرض
    /// كارت واحد يضمن عدد أعمدة ثابت (5 افتراضيًا) — بدل تخمين رقم عرض
    /// ثابت بيصح على شاشة ويغلط على تانية. الحل التاني (اللي جرّبناه الأول)
    /// كان عرض 260px تقريبي، وطلع بيدّي 4 أعمدة بس على شاشة المستخدم —
    /// الحساب هنا مضمون مهما كان حجم النافذة.
    /// </summary>
    public class GridColumnWidthConverter : IValueConverter
    {
        /// <summary>هامش كل كارت (يمين+تحت، Margin="0,0,10,10" في WorkersView.xaml) — لازم يتوافق مع القيمة الفعلية في XAML</summary>
        private const double CardMargin = 10;

        /// <summary>حد أدنى لعرض الكارت — أي حاجة أضيق من كده بتكسر قراءة الاسم/البادجات</summary>
        private const double MinCardWidth = 170;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // القيمة الافتراضية قبل أول تمرير Layout حقيقي (ActualWidth بيبقى صفر لحظة الإنشاء)
            if (value is not double containerWidth || containerWidth <= 0) return 250d;

            var columns = parameter is string s && int.TryParse(s, out var parsed) && parsed > 0 ? parsed : 5;
            var width = (containerWidth - columns * CardMargin) / columns;

            return Math.Max(MinCardWidth, width);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
