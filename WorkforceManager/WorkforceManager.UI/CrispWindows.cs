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

            ApplyScale(window);

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

        /// <summary>
        /// بيكبّر الديالوج بنفس مقياس النافذة الرئيسية.
        ///
        /// **ليه**: التكبير متطبّق على شجرة MainWindow، والديالوجات نوافذ
        /// منفصلة برّه الشجرة دي — فكانت بتطلع بحجمها الأصلي فوق تطبيق
        /// مكبّر. الفرق 30% على شاشة 1080 و65% على 4K.
        ///
        /// MainWindow نفسها مستثناة: بتكبّر جواها بـ LayoutTransform على
        /// الشبكة الجذرية، وتكبيرها هنا كمان كان هيضاعف المقياس.
        /// </summary>
        private static void ApplyScale(Window window)
        {
            if (window is MainWindow) return;

            var scale = MainWindow.CurrentScale;
            if (scale <= 1.0001) return; // مفيش تكبير — مفيش داعي نلمس حاجة

            if (window.Content is not FrameworkElement content) return;
            if (content.LayoutTransform is ScaleTransform) return; // اتطبّق قبل كده

            content.LayoutTransform = new ScaleTransform(scale, scale);

            // العرض والارتفاع مكتوبين على النافذة نفسها مش على المحتوى،
            // فالـ LayoutTransform لوحده كان هيكبّر المحتوى جوه إطار بحجمه
            // القديم ويقصّه. NaN معناها المقاس بيتحدد من المحتوى
            // (SizeToContent) وساعتها بيتظبط لوحده.
            var area = SystemParameters.WorkArea;

            if (!double.IsNaN(window.Width))
                window.Width = Math.Min(window.Width * scale, area.Width);

            if (!double.IsNaN(window.Height))
                window.Height = Math.Min(window.Height * scale, area.Height);

            // تغيير المقاس بعد ما النافذة اتعرضت بيسيبها مزحلقة عن مكانها،
            // فبنرجّعها للنص بنفسنا
            Recenter(window);
        }

        private static void Recenter(Window window)
        {
            // بنستنى الترتيب يخلص عشان ActualWidth/Height تبقى المقاس الجديد
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var area = SystemParameters.WorkArea;

                var owner = window.Owner;
                var bounds = owner is { IsLoaded: true }
                    ? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight)
                    : area;

                window.Left = bounds.Left + (bounds.Width - window.ActualWidth) / 2;
                window.Top = bounds.Top + (bounds.Height - window.ActualHeight) / 2;

                // ميخرجش برّه الشاشة لو الأب كان على حرفها
                window.Left = Math.Max(area.Left, Math.Min(window.Left, area.Right - window.ActualWidth));
                window.Top = Math.Max(area.Top, Math.Min(window.Top, area.Bottom - window.ActualHeight));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
