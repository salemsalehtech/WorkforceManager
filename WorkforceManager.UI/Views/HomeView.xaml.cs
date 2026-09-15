using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الرئيسية" — التنقّل منها بيحصل بنفس أسلوب
    /// MainWindow.OfferLearnFeaturesIfNew الموجود أصلاً: تحديد
    /// IsChecked على عنصر القائمة الجانبية المطلوب بدل أي آلية جديدة،
    /// عشان يشتغل مؤشر التنقّل الدهبي وتحديث البادچات زي أي تنقّل عادي.
    ///
    /// الحركة (دخول الكروت المتدرّج، عدّ الأرقام لفوق، نبضة الكأس) كلها
    /// هنا مش في الـViewModel عن قصد — تأثير بصري بحت مالوش أي داعي
    /// يوصّل لطبقة العرض المنطقي، ومفيش اختبار محتاج يغطّيه.
    /// </summary>
    public partial class HomeView : UserControl
    {
        private readonly HomeViewModel _viewModel;

        public HomeView(HomeViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            // الحركة بعد ما التحميل يخلص، مش قبله — الكروت بتتحرك بأرقامها
            // النهائية جاهزة جواها، مش فاضية وبتتحرك بعدين تتملى فجأة
            Loaded += async (_, _) =>
            {
                await viewModel.LoadAsync();
                AnimateCardsIn();
                AnimateNumbers();
            };
        }

        /// <summary>
        /// دخول متدرّج (Opacity + انزياح لأعلى) لكل كارت + زرار الفعل
        /// الرئيسي — كارت وراء التاني بفاصل 90ms، نفس منحنى CubicEase
        /// EaseOut المستخدم في كل حركة تانية بالمشروع.
        /// </summary>
        private void AnimateCardsIn()
        {
            var targets = new FrameworkElement[]
            {
                WeeklyStatsCard, BestWorkerCard, AttentionCard, StartProductionDayButton
            };

            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                var beginTime = TimeSpan.FromMilliseconds(i * 90);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var fade = new DoubleAnimation
                {
                    From = 0, To = 1, BeginTime = beginTime,
                    Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease
                };
                target.BeginAnimation(OpacityProperty, fade);

                if (target.RenderTransform is TranslateTransform translate)
                {
                    var slide = new DoubleAnimation
                    {
                        From = 14, To = 0, BeginTime = beginTime,
                        Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease
                    };
                    translate.BeginAnimation(TranslateTransform.YProperty, slide);
                }
                else if (target.RenderTransform is TransformGroup group)
                {
                    var translateInGroup = group.Children.OfType<TranslateTransform>().First();
                    var slide = new DoubleAnimation
                    {
                        From = 14, To = 0, BeginTime = beginTime,
                        Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease
                    };
                    translateInGroup.BeginAnimation(TranslateTransform.YProperty, slide);
                }
            }

            // نبضة الكأس مرتين بس لو فيه أحسن عامل فعلاً — نفس فكرة نبضة
            // زرار طي الشريط أول مرة (SidebarToggleHintShown في MainWindow)،
            // بس هنا بتتكرر كل ما الشاشة تتحمّل لأنها احتفال بنتيجة حية
            // مش تعريف بزرار جديد
            if (_viewModel.HasBestWorker && TrophyIcon.RenderTransform is ScaleTransform trophyScale)
            {
                var pulse = new DoubleAnimation
                {
                    From = 1, To = 1.25, BeginTime = TimeSpan.FromMilliseconds(300),
                    Duration = TimeSpan.FromMilliseconds(260), AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                trophyScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                trophyScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
        }

        /// <summary>
        /// الأرقام البطلة (القطع/العمال/اليوميات/نسبة الحضور) بتعدّ من صفر
        /// للرقم الحقيقي بدل ما تظهر جاهزة فجأة — DispatcherTimer بسيط
        /// بدل Storyboard/AnimationClock لأن الهدف مجرد نص Run.Text، مش
        /// خاصية DependencyProperty حقيقية قابلة للـStoryboard مباشرة.
        /// </summary>
        private void AnimateNumbers()
        {
            AnimateCountUp(TotalPiecesRun, _viewModel.TotalPiecesThisWeek);
            AnimateCountUp(ActiveWorkersRun, _viewModel.ActiveWorkersThisWeek);
            AnimateCountUp(NetWorkdaysRun, (double)_viewModel.NetWorkdaysThisWeek, v => v.ToString("N1"));

            if (_viewModel.AttendanceRatePercent is { } rate)
                AnimateCountUp(AttendanceRateRun, (double)rate);
        }

        private static void AnimateCountUp(Run target, double to, Func<double, string>? format = null)
        {
            format ??= v => Math.Round(v).ToString("N0");

            if (to == 0) { target.Text = format(0); return; }

            const int durationMs = 650;
            var start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var t = Math.Min(1.0, (DateTime.UtcNow - start).TotalMilliseconds / durationMs);
                var eased = 1 - Math.Pow(1 - t, 3); // ease-out تكعيبي، نفس منحنى CubicEase المستخدم في باقي المشروع
                target.Text = format(to * eased);

                if (t >= 1.0)
                {
                    timer.Stop();
                    target.Text = format(to);
                }
            };
            timer.Start();
        }

        private void StartProductionDay_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.NavDailyEntryItem.IsChecked = true;
        }

        private void GoToProducts_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.NavProductsItem.IsChecked = true;
        }

        private void GoToMemory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.NavMemoryItem.IsChecked = true;
        }
    }
}
