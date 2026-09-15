using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة العمال: الكود هنا شكلي بس (ربط الـ ViewModel) — كل المنطق
    /// في WorkersViewModel حسب نمط MVVM المتبع في المشروع. زائد حركة دخول
    /// كروت الشبكة، منقولة ومُكيَّفة من ProductsView.xaml.cs's
    /// AnimateTilesIn (شوف تعليقها هناك للتفاصيل الكاملة عن الآلية).
    /// </summary>
    public partial class WorkersView : UserControl
    {
        /// <summary>
        /// true لو حركة مجدولة لسه مستنية تتنفذ — بيمنع تكرار الحركة لكل
        /// عنصر بيتضاف/يتشال من Workers وقت ApplyFilters (Clear ثم N من
        /// Add)، فبدل حركة لكل عملية إضافة منفردة، الكل بيتجمّع في تشغيلة
        /// واحدة بعد ما الفلترة كلها تخلص.
        /// </summary>
        private bool _tileAnimationPending;

        public WorkersView(WorkersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Workers بتتغيّر مع كل تحميل أول مرة وكل بحث/فلتر/ترتيب بعد
            // كده (ApplyFilters)، فالحركة لازم تتكرر كل مرة مش تشتغل مرة
            // واحدة بس عند Loaded
            viewModel.Workers.CollectionChanged += (_, _) => ScheduleTileAnimation();

            // تحميل البيانات أول ما الشاشة تظهر (مش في الـ Constructor عشان الواجهة متعلقش)
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }

        /// <summary>
        /// دوسة على كارت عامل في الشبكة: بيحدد العامل (SelectWorkerCommand)
        /// وبيفتح تفاصيله Modal دايركت — نفس أسلوب ProductTile_Click بالحرف.
        /// الـDialog بياخد نفس الـViewModel (مش نسخة تانية) عشان كل أوامره
        /// تفضل شغالة زي ما هي.
        /// </summary>
        private void WorkerTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WorkerRow worker }) return;
            if (DataContext is not WorkersViewModel viewModel) return;

            viewModel.SelectWorkerCommand.Execute(worker);

            var dialog = new WorkerDetailDialog(viewModel) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }

        // ------- القائمة السياقية على الكارت (زرار يمين) -------
        // ContextMenu.DataContext اتظبط صراحة في XAML على
        // PlacementTarget.DataContext (WorkerRow) — نفس أسلوب WorkerTile_Click
        // بالظبط: نجيب WorkerRow من الـsender، والـViewModel من DataContext
        // الشاشة نفسها مباشرة، بدل أي محاولة نربط الأمر جوّه XAML على
        // WorkersViewModel من جوّه ContextMenu (اللي مش وارث DataContext
        // من مالكه أصلًا، شوف تعليق XAML).

        private void EditWorkerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WorkerRow worker }) return;
            if (DataContext is not WorkersViewModel viewModel) return;

            viewModel.EditWorkerFromCardCommand.Execute(worker);
        }

        private void ToggleActiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WorkerRow worker }) return;
            if (DataContext is not WorkersViewModel viewModel) return;

            viewModel.ToggleActiveFromCardCommand.Execute(worker);
        }

        private void DeleteWorkerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WorkerRow worker }) return;
            if (DataContext is not WorkersViewModel viewModel) return;

            viewModel.DeleteWorkerFromCardCommand.Execute(worker);
        }

        /// <summary>
        /// بيجمّع كل تغييرات Workers المتتالية (Clear ثم عدة Add) في تشغيلة
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
        /// آلية ProductsView.AnimateTilesIn بالظبط: تأخير البداية لكل كارت
        /// حسب ترتيبه (index × 60ms)، تلاشي + انزلاق لأعلى 280ms بـ EaseOut.
        /// </summary>
        private void AnimateTilesIn()
        {
            WorkersGrid.UpdateLayout();

            for (var i = 0; i < WorkersGrid.Items.Count; i++)
            {
                if (WorkersGrid.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter presenter)
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
