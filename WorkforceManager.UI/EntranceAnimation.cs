using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkforceManager.UI
{
    /// <summary>
    /// دخول الشاشة كلها مرة واحدة (تلاشي + انزلاق خفيف لفوق) — للشاشات
    /// اللي مش شبكة كروت. نفس قيم حركة الكروت المتدرّجة بالظبط
    /// (WorkersView/ProductsView.AnimateTilesIn: 280ms، 14px، EaseOut) بس
    /// على الشاشة ككل من غير تدرّج، عشان تبان نفس لغة الحركة.
    ///
    /// بتتنادى من Loaded بتاع الشاشة نفسها — مش من MainWindow — فأي مكان
    /// بيحط الشاشة في MainContent (زراير القائمة، فتح التسجيل من الذاكرة،
    /// وضع التجربة) بيشغّلها من غير ربط إضافي. شاشة الدخول والديالوجات
    /// مالهاش علاقة بيها.
    ///
    /// **الجولة**: RunTourAsync بيستنى قبل ما يقيس مكان العنصر المستهدف
    /// (TransformToVisual بيحسب RenderTransform) — المدة دي لازم تفضل
    /// أطول من DurationMs هنا، وإلا الإضاءة بتقع على عنصر لسه بيتحرك.
    /// </summary>
    public static class EntranceAnimation
    {
        public const int DurationMs = 280;
        private const double SlideFrom = 14;

        public static void PlayFadeSlideIn(FrameworkElement root)
        {
            // Transform جديد في الكود كل مرة، مش متعرّف في XAML: WPF بيجمّد
            // أي Freezable قيمه ثابتة في XAML وBeginAnimation بيرمي عليه
            var slide = new TranslateTransform(0, SlideFrom);
            root.RenderTransform = slide;

            var duration = TimeSpan.FromMilliseconds(DurationMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            // بعد ما تخلص بتفك الحركة وتثبّت القيم النهائية — من غير كده
            // الحركة بتفضل ماسكة Opacity وأي كود تاني يحاول يغيّرها مابيأثرش
            fade.Completed += (_, _) =>
            {
                root.BeginAnimation(UIElement.OpacityProperty, null);
                root.Opacity = 1;
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = 0;
            };

            root.BeginAnimation(UIElement.OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation { From = SlideFrom, To = 0, Duration = duration, EasingFunction = ease });
        }
    }
}
