using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// بيقارن معرّف عنصر في قايمة بمعرّف "اللي بيتعدّل دلوقتي" في الـ
    /// ViewModel — الاتنين جايين من MultiBinding (شوف MemoryView.xaml)،
    /// ومفيش طريقة تقارن Bindings ببعض غير كده أو بمحوّل مخصص.
    /// بيرجّع سُمك حدّ (2 لو متطابقين، صفر لو لأ) — الفرشة نفسها ثابتة
    /// على الكارت دايمًا، فسُمك صفر معناها الحدّ مش ظاهر أصلًا.
    /// </summary>
    public class EditingCardBorderConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int itemId && values[1] is int editingId && itemId == editingId)
                return new Thickness(2);

            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
