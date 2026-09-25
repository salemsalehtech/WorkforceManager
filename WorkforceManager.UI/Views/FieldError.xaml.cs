using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// خطأ خانة واحدة، بيظهر تحتها مباشرة — الكونترول الوحيد لأخطاء الخانات
    /// في الفورمات اللي جوّه الشاشات (مش إشعار طاير يقطع الإدخال). بيختفي
    /// لوحده لما Message تفضى. الـViewModel بيحط الرسالة وقت الحفظ ويمسحها
    /// أول ما الخانة تتعدّل — شوف FieldRules وCLAUDE.md.
    /// </summary>
    public partial class FieldError : UserControl
    {
        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            nameof(Message), typeof(string), typeof(FieldError),
            new PropertyMetadata("", (d, e) =>
                ((FieldError)d).Visibility = string.IsNullOrEmpty(e.NewValue as string)
                    ? Visibility.Collapsed
                    : Visibility.Visible));

        public FieldError() => InitializeComponent();

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }
    }
}
