using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة المنتجات والمراحل: كود الخلف هنا شكلي — ربط الـ ViewModel، زائد
    /// حركة دخول كروت الشبكة (منقولة ومُكيَّفة من HelpView.xaml.cs's
    /// AnimateTilesIn، شوف تعليقها هناك للتفاصيل الكاملة عن الآلية).
    /// </summary>
    public partial class ProductsView : UserControl
    {
        /// <summary>
        /// true لو حركة مجدولة لسه مستنية تتنفذ — بيمنع تكرار الحركة لكل
        /// عنصر بيتضاف/يتشال من Products وقت ApplyFilter (Clear ثم N من
        /// Add، كل واحدة بتطلق CollectionChanged لوحدها)، فبدل حركة لكل
        /// عملية إضافة منفردة، الكل بيتجمّع في تشغيلة واحدة بعد ما الفلترة
        /// كلها تخلص.
        /// </summary>
        private bool _tileAnimationPending;

        public ProductsView(ProductsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Products بتتغيّر مع كل تحميل أول مرة وكل بحث/فلتر/فترة بعد
            // كده (ApplyFilter) — عكس شبكة "الدليل" الثابتة، فالحركة هنا
            // لازم تتكرر كل مرة، مش تشتغل مرة واحدة بس عند Loaded
            viewModel.Products.CollectionChanged += (_, _) => ScheduleTileAnimation();

            // تحميل المنتجات أول ما الشاشة تظهر
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }

        /// <summary>
        /// بيجمّع كل تغييرات Products المتتالية (Clear ثم عدة Add) في تشغيلة
        /// حركة واحدة بس — بيأجّل التنفيذ لخطوة Dispatcher تالية (أولوية
        /// Loaded، بعد التخطيط) عشان WrapPanel يكون خلّص ترتيب صفوفه الجديد
        /// قبل ما نلوّن العناصر.
        /// </summary>
        private void ScheduleTileAnimation()
        {
            if (_tileAnimationPending) return;
            _tileAnimationPending = true;

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                _tileAnimationPending = false;
                AnimateTilesIn();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// حركة دخول متدرّجة حقيقية (Storyboard، مش حلقة Task.Delay) — نفس
        /// آلية HelpView.AnimateTilesIn بالظبط: تأخير البداية لكل كارت حسب
        /// ترتيبه (index × 60ms)، تلاشي + انزلاق لأعلى 280ms بـ EaseOut.
        /// </summary>
        private void AnimateTilesIn()
        {
            ProductsGrid.UpdateLayout();

            for (var i = 0; i < ProductsGrid.Items.Count; i++)
            {
                if (ProductsGrid.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                    continue;

                presenter.ApplyTemplate();
                if (VisualTreeHelper.GetChild(presenter, 0) is not FrameworkElement tile) continue;

                var beginTime = TimeSpan.FromMilliseconds(i * 60);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var fadeIn = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(280),
                    BeginTime = beginTime, EasingFunction = ease
                };
                Storyboard.SetTarget(fadeIn, tile);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

                var slideUp = new DoubleAnimation
                {
                    From = 14, To = 0, Duration = TimeSpan.FromMilliseconds(280),
                    BeginTime = beginTime, EasingFunction = ease
                };
                Storyboard.SetTarget(slideUp, tile);
                Storyboard.SetTargetProperty(slideUp,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeIn);
                storyboard.Children.Add(slideUp);
                storyboard.Begin();
            }
        }
    }
}
