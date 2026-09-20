using System.Windows;
using System.Windows.Media.Animation;

namespace WorkforceManager.UI
{
    /// <summary>
    /// بيربط قيمة محسوبة (زي عرض كارت من GridColumnWidthConverter) بخاصية
    /// Width الحقيقية بحركة ناعمة بدل قفزة فورية. Binding عادي على Width
    /// بيغيّر القيمة فجأة كل ما المصدر يتغيّر — الخاصية المرفقة دي بتحوّل
    /// أي تغيير لـDoubleAnimation قصيرة بدل كده، من العرض الحالي الفعلي
    /// (ActualWidth) للعرض الجديد.
    ///
    /// استخدامها: بدل Width="{Binding ...}"، تحط
    /// ui:AnimatedWidth.Value="{Binding ...}" (أو MultiBinding) — العنصر
    /// نفسه بياخد الـWidth الفعلي من الحركة، مش من Binding مباشر.
    ///
    /// نفس مدة/منحنى حركة طي الشريط الجانبي بالظبط (AnimateSidebarCollapse
    /// في MainWindow.xaml.cs) عشان الكروت تتحرك متزامنة بصريًا مع الشريط.
    /// </summary>
    public static class AnimatedWidth
    {
        private const int DurationMs = 220;

        public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
            "Value", typeof(double), typeof(AnimatedWidth),
            new PropertyMetadata(double.NaN, OnValueChanged));

        public static double GetValue(DependencyObject obj) => (double)obj.GetValue(ValueProperty);
        public static void SetValue(DependencyObject obj, double value) => obj.SetValue(ValueProperty, value);

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;
            if (e.NewValue is not double newWidth || double.IsNaN(newWidth) || newWidth <= 0) return;

            // أول قيمة (وقت إنشاء الكارت لسه، مفيش ActualWidth حقيقي) — تتحط
            // مباشرة من غير حركة، وإلا كل كارت جديد هيدخل بحركة اتساع من صفر
            var fromWidth = element.ActualWidth > 0 ? element.ActualWidth : newWidth;

            if (Math.Abs(fromWidth - newWidth) < 0.5)
            {
                element.Width = newWidth;
                return;
            }

            var animation = new DoubleAnimation
            {
                From = fromWidth,
                To = newWidth,
                Duration = TimeSpan.FromMilliseconds(DurationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }
    }
}
