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

        /// <summary>
        /// true لو فيه كارت لسه بيتقلب — بيمنع أي كارت (نفسه أو غيره) من
        /// بدء قلب جديد لحد ما ده يخلص. من غيرها دبل-كليك سريع كان ممكن
        /// يسيب الكارت واقف نص قلبة (ScaleX في نص الطريق) أو يعرض الوش
        /// الغلط لو نداءين اتزنقوا فوق بعض.
        /// </summary>
        private bool _flipAnimating;

        private const int FlipHalfDurationMs = 140;

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
        /// دوسة على كارت عامل في الشبكة: بتقلبه بحركة (AnimateFlip) —
        /// مبقتش بتفتح الديالوج على طول، ده بقى شغل زرار "افتح الملف
        /// الكامل" على الوش التاني (OpenFullProfile_Click تحت).
        /// </summary>
        private void WorkerTile_Click(object sender, RoutedEventArgs e)
        {
            if (_flipAnimating) return; // كارت تاني (أو نفسه) لسه بيتقلب
            if (sender is not Button { DataContext: WorkerRow worker } button) return;
            if (DataContext is not WorkersViewModel viewModel) return;

            AnimateFlip(button, () => viewModel.ToggleFlipCommand.Execute(worker));
        }

        /// <summary>
        /// حركة القلب: تصغير الـScaleX لصفر (نص عرض تقريبًا زمنيًا)، وعند
        /// الوصول لصفر (العرض بقى خط رفيع، الوش القديم مش باين أصلًا)
        /// تنفيذ toggleFlip (بيبدّل IsFlipped فيبدّل مين الظاهر بالـBinding
        /// جوّه DataTemplate)، وبعدها تكبير الـScaleX تاني لـ1 — عنصر واحد
        /// بيتحرك (الـGrid الحاوي للوشين، شوف تعليق WorkersView.xaml)، مش
        /// وش لوحده وتاني لوحده، فمفيش مزامنة بين Storyboard-ين منفصلين.
        ///
        /// الوصول للـGrid عن طريق Content/Child (خصائص object model عادية)
        /// مش VisualTreeHelper جوّه قالب CardButton — أبسط ومش مربوط
        /// بتفاصيل الـControlTemplate اللي ممكن تتغيّر.
        ///
        /// ScaleTransform بتتعمل هنا في الكود كل مرة، مش بتتقرا من XAML:
        /// WPF بيجمّد Freezable قيمه ثابتة من غير Binding (زي ScaleX=1
        /// المكتوبة في XAML) كتحسين أداء، وBeginAnimation بيرمي استثناء
        /// على أي حاجة متجمّدة ("Cannot animate ... sealed or frozen").
        /// نسخة جديدة في الكود كل قلبة تفضل قابلة للتعديل مضمون.
        /// </summary>
        private void AnimateFlip(Button cardButton, Action toggleFlip)
        {
            if (cardButton.Content is not Border { Child: Grid flipHost }) return;

            var scale = new ScaleTransform(1, 1);
            flipHost.RenderTransform = scale;

            _flipAnimating = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var shrink = new DoubleAnimation
            {
                From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(FlipHalfDurationMs),
                EasingFunction = ease
            };
            shrink.Completed += (_, _) =>
            {
                toggleFlip();

                var grow = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(FlipHalfDurationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                grow.Completed += (_, _) => _flipAnimating = false;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        }

        /// <summary>
        /// زرار "افتح الملف الكامل" على وش الكارت التاني — نفس اللي
        /// WorkerTile_Click كان بيعمله قبل ما يبقى للقلب: يحدد العامل
        /// ويفتح تفاصيله Modal، نفس الـViewModel (مش نسخة تانية) عشان
        /// كل أوامره تفضل شغالة زي ما هي.
        ///
        /// e.Handled = true إجباري: الزرار ده جوّه الزرار الأكبر بتاع
        /// الكارت (نفس أسلوب Button-جوّه-Button)، وClick بيطلع Bubble
        /// للأب افتراضيًا في WPF — من غيرها كان هيقلب الكارت **كمان**
        /// في نفس اللحظة اللي بيفتح فيها الديالوج.
        /// </summary>
        private void OpenFullProfile_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

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
