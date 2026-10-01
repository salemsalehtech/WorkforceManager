using System.Globalization;
using System.Windows.Data;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// قيمة enum = الاسم اللي في ConverterParameter؟ — لـ IsChecked بتاع chips الفلتر
    /// (OneWay بس؛ الاختيار نفسه بيتعمل بـ Command، فمفيش ConvertBack).
    /// </summary>
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is not null && parameter is not null && value.ToString() == parameter.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>عكس bool — لـ IsChecked بتاع زرار "كروت" قصاد IsTableMode</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    }
}
