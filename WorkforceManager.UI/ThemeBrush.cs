using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;

namespace WorkforceManager.UI
{
    /// <summary>
    /// بيربط لون عنصر بمفتاح من لوحة الألوان **باسمه**، والاسم جاي من
    /// الـ ViewModel.
    ///
    /// ليه ده موجود: الـ ViewModels كانت بترجّع كود لون مكتوب بالإيد
    /// (\u200E"#0B6E4F"\u200E أخضر، \u200E"#B00020"\u200E أحمر). ده كان بيكسر حاجتين:
    ///   1. **الثيم**: الكود الثابت مبيتغيرش، فالوضع الليلي كان بيطلع
    ///      بنفس ألوان النهاري بالظبط — أخضر فاقع وأحمر فاقع على غامق.
    ///   2. **الهوية**: ألوان بتتولد بره اللوحة، يعني مفيش مصدر واحد
    ///      للحقيقة مهما ظبطنا الملفات.
    ///
    /// الحل مش إن الـ ViewModel يرجّع لون تاني — هو إنه **ميرجّعش ألوان
    /// أصلًا**. بيرجّع اسم الدور ("GoodBrush"\u200E / \u200E"DangerBrush"\u200E)، واللوحة
    /// هي اللي بتقرر اللون ده يبقى إيه في كل ثيم.
    ///
    /// وبنستخدم SetResourceReference مش Brush جاهزة عن قصد: دي المقابل
    /// البرمجي لـ DynamicResource، فالربط بيفضل **حيّ** وتبديل الثيم
    /// بيوصل للعنصر في اللحظة. محوّل عادي كان هيرجّع فرشاة ميتة.
    /// </summary>
    public static class ThemeBrush
    {
        /// <summary>مفتاح فرشاة من اللوحة للون النص/الأيقونة.</summary>
        public static readonly DependencyProperty ForegroundKeyProperty =
            DependencyProperty.RegisterAttached(
                "ForegroundKey", typeof(string), typeof(ThemeBrush),
                new PropertyMetadata(null, OnForegroundKeyChanged));

        public static void SetForegroundKey(DependencyObject element, string? value)
            => element.SetValue(ForegroundKeyProperty, value);

        public static string? GetForegroundKey(DependencyObject element)
            => (string?)element.GetValue(ForegroundKeyProperty);

        /// <summary>مفتاح فرشاة من اللوحة لخلفية العنصر.</summary>
        public static readonly DependencyProperty BackgroundKeyProperty =
            DependencyProperty.RegisterAttached(
                "BackgroundKey", typeof(string), typeof(ThemeBrush),
                new PropertyMetadata(null, OnBackgroundKeyChanged));

        public static void SetBackgroundKey(DependencyObject element, string? value)
            => element.SetValue(BackgroundKeyProperty, value);

        public static string? GetBackgroundKey(DependencyObject element)
            => (string?)element.GetValue(BackgroundKeyProperty);

        /// <summary>مفتاح فرشاة من اللوحة لحد العنصر.</summary>
        public static readonly DependencyProperty BorderKeyProperty =
            DependencyProperty.RegisterAttached(
                "BorderKey", typeof(string), typeof(ThemeBrush),
                new PropertyMetadata(null, OnBorderKeyChanged));

        public static void SetBorderKey(DependencyObject element, string? value)
            => element.SetValue(BorderKeyProperty, value);

        public static string? GetBorderKey(DependencyObject element)
            => (string?)element.GetValue(BorderKeyProperty);

        private static void OnForegroundKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // كل نوع بيسمّي خاصية اللون بتاعته باسم مختلف، والخاصية
            // بتبقى معرّفة على النوع نفسه — فمينفعش مفتاح واحد للكل.
            var property = d switch
            {
                TextBlock => TextBlock.ForegroundProperty,
                Control => Control.ForegroundProperty,
                Shape => Shape.FillProperty,
                TextElement => TextElement.ForegroundProperty,
                _ => null
            };

            Apply(d, property, e.NewValue as string);
        }

        private static void OnBackgroundKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var property = d switch
            {
                Border => Border.BackgroundProperty,
                Panel => Panel.BackgroundProperty,
                Control => Control.BackgroundProperty,
                TextBlock => TextBlock.BackgroundProperty,
                _ => null
            };

            Apply(d, property, e.NewValue as string);
        }

        private static void OnBorderKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var property = d switch
            {
                Border => Border.BorderBrushProperty,
                Control => Control.BorderBrushProperty,
                Shape => Shape.StrokeProperty,
                _ => null
            };

            Apply(d, property, e.NewValue as string);
        }

        private static void Apply(DependencyObject target, DependencyProperty? property, string? key)
        {
            if (property is null) return;

            if (target is not FrameworkElement && target is not FrameworkContentElement) return;

            if (string.IsNullOrWhiteSpace(key))
            {
                // مفتاح فاضي = ارجع للقيمة الأصلية بتاعة النمط، مش للأسود
                target.ClearValue(property);
                return;
            }

            // المقابل البرمجي لـ DynamicResource — الربط بيفضل حيّ
            // فتبديل الثيم بيوصل من غير إعادة تحميل الشاشة
            if (target is FrameworkElement element) element.SetResourceReference(property, key);
            else ((FrameworkContentElement)target).SetResourceReference(property, key);
        }
    }
}
