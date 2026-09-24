using System.Collections.Generic;
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
    ///
    /// الشبكة بقت مقسّمة لأقسام (عيلة لكل قسم، شوف ProductsView.xaml)،
    /// فمفيش ItemsControl واحد بس للكروت — الحركة بتدور على شجرة العرض
    /// كلها (FindVisualChildren) عشان تلاقي كل الـItemsControl الداخلية
    /// (كارتات المنتجات نفسها)، وindex الحركة تراكمي عبر الأقسام كلها
    /// (مش بيتصفّر لكل قسم) عشان التتابع البصري يفضل متدرج قسم بعد قسم.
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

            // FamilyGroups (مش Products) هي اللي فعليًا بتبني شكل الشاشة —
            // بتتغيّر مع كل تحميل أول مرة وكل بحث/فلتر/فترة بعد كده
            // (ApplyFilter → RebuildFamilyGroups)
            viewModel.FamilyGroups.CollectionChanged += (_, _) => ScheduleTileAnimation();

            // تحميل المنتجات أول ما الشاشة تظهر
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }

        /// <summary>
        /// دوسة على كارت منتج في الشبكة: بيحدد المنتج (نفس أمر التحديد
        /// القديم) وبيفتح تفاصيله Modal دايركت — بدل ما تنزل تحت الشبكة
        /// زي التصميم القديم. الـ Dialog بياخد نفس الـ ViewModel (مش نسخة
        /// تانية) عشان كل أوامره تفضل شغالة زي ما هي.
        /// </summary>
        private void ProductTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ProductRow product }) return;
            if (DataContext is not ProductsViewModel viewModel) return;

            viewModel.SelectProductCommand.Execute(product);

            var dialog = new ProductDetailDialog(viewModel) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }

        /// <summary>
        /// بيجمّع كل تغييرات FamilyGroups المتتالية (Clear ثم عدة Add) في
        /// تشغيلة حركة واحدة بس — بيأجّل التنفيذ لخطوة Dispatcher تالية
        /// (أولوية Loaded، بعد التخطيط) عشان WrapPanel يكون خلّص ترتيب
        /// صفوفه الجديد قبل ما نلوّن العناصر.
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
        /// ترتيبه التراكمي (index × 60ms) عبر كل الأقسام، تلاشي + انزلاق
        /// لأعلى 280ms بـEaseOut.
        /// </summary>
        private void AnimateTilesIn()
        {
            FamilyGroupsList.UpdateLayout();

            var index = 0;
            foreach (var innerGrid in FindVisualChildren<ItemsControl>(FamilyGroupsList))
            {
                if (innerGrid.Name != "ProductsGrid") continue;

                for (var i = 0; i < innerGrid.Items.Count; i++)
                {
                    if (innerGrid.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
                        continue;

                    presenter.ApplyTemplate();
                    if (VisualTreeHelper.GetChild(presenter, 0) is not FrameworkElement tile) continue;

                    var beginTime = TimeSpan.FromMilliseconds(index * 60);
                    index++;
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

        /// <summary>دوران بسيط على شجرة العرض عن كل عنصر من نوع معيّن — مفيش helper عام مشترك في المشروع، كل شاشة بنسختها المحلية</summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var grandChild in FindVisualChildren<T>(child)) yield return grandChild;
            }
        }
    }
}
