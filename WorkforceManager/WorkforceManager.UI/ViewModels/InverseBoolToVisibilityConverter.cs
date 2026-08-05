using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عكس BooleanToVisibilityConverter: true = مخفي، false = ظاهر.
    ///
    /// بيتستخدم لما عنصرين بيتبادلوا المكان على نفس الشرط — واحد بـ
    /// BoolToVis والتاني بده — بدل ما نضيف خاصية مقلوبة في كل DTO.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility.Collapsed;
    }
}
