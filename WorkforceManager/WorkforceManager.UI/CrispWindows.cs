using System.Windows;
using System.Windows.Media;

namespace WorkforceManager.UI
{
    /// <summary>
    /// بيخلي **كل** نافذة في البرنامج ترسم حادّة على الشاشات المكبّرة.
    ///
    /// المشكلة اللي بيحلها: الإعدادات دي كانت متكتوبة في MainWindow.xaml
    /// بس (والتعليق هناك بيشرح ليه)، والـ 30 نافذة التانية — كل ديالوج في
    /// البرنامج — كانت بترسم من غيرها. ستايل ضمني `TargetType="Window"`
    /// **مبيوصلش** ليهم لأنهم كلهم كلاسات مشتقة، وده فخ موثّق في CLAUDE.md
    /// وبيضرب في تلات مواضع تانية في المشروع.
    ///
    /// أثر غيابها بيبان في الديالوجات أكتر من أي حتة تانية: كلها
    /// CornerRadius="18" مع DropShadowEffect وحدود رفيعة، وكل دول بيترسموا
    /// على إحداثيات كسرية عند أي تكبير غير 100% (125% هو الافتراضي على
    /// أغلب لابتوبات ويندوز) — فالحواف والزوايا بتطلع باهتة ومتغبّشة.
    ///
    /// **RegisterClassHandler مش ستايل**: بينادَى لكل نسخة من Window
    /// وكل المشتق منها، فمفيش ديالوج ينفع ينساه — ولا الديالوجات
    /// الموجودة ولا أي واحد يتضاف بعد كده.
    /// </summary>
    public static class CrispWindows
    {
        /// <summary>
        /// بيتنادى مرة واحدة عند بدء التشغيل، قبل ما أي نافذة تتعمل.
        /// </summary>
        public static void Enable()
        {
            EventManager.RegisterClassHandler(
                typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;

            // MainWindow حاططهم في الـ XAML بتاعها خلاص — ومفيش ضرر من
            // إعادة الضبط، بس بنسيبها عشان مصدر القيمة يفضل واضح هناك
            if (window.ReadLocalValue(FrameworkElement.UseLayoutRoundingProperty)
                == DependencyProperty.UnsetValue)
                window.UseLayoutRounding = true;

            // الاتنين دول بيتوارثوا لكل الشجرة تحت النافذة، فحطّهم على
            // الجذر بيغطي كل عنصر جوّاها
            if (window.ReadLocalValue(TextOptions.TextFormattingModeProperty)
                == DependencyProperty.UnsetValue)
                TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);

            if (window.ReadLocalValue(TextOptions.TextRenderingModeProperty)
                == DependencyProperty.UnsetValue)
                TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
        }
    }
}
