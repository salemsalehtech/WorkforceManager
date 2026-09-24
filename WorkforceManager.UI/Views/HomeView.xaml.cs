using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الرئيسية" — كل تنقّل منها بيعدّي على نفس آلية الشريط الجانبي
    /// (IsChecked على عنصر التنقل، أو LandOnWorkerAsync/LandOnProduct بتوع
    /// البحث السريع للهبوط على عامل/منتج بعينه)، مش مسار تنقّل موازي —
    /// فالمؤشر الدهبي والبادچات بيتحدّثوا زي أي تنقّل عادي.
    ///
    /// الحركة كلها هنا مش في الـViewModel عن قصد — تأثير بصري بحت.
    /// </summary>
    public partial class HomeView : UserControl
    {
        /// <summary>
        /// الدخول الكامل (انزلاق متدرّج + عدّ الأرقام + نبضة الكأس) مرة واحدة بس
        /// في كل تشغيل للبرنامج. HomeView مسجّلة Transient فبتتبني من جديد كل
        /// رجوع للرئيسية — من غير العلم ده الشاشة كانت هتعيد العرض كله كل
        /// مرة، وده بيزهق في شاشة المستخدم بيرجعلها طول اليوم. الرجوع بعد
        /// كده ظهور سريع بس.
        /// </summary>
        private static bool s_entrancePlayed;

        /// <summary>تحت العرض ده (بعد طي الشريط) الأرقام بتبقى عمودين والناس بتنزل تحت الرسم</summary>
        private const double NarrowWidth = 980;

        private readonly HomeViewModel _viewModel;

        public HomeView(HomeViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);

            // الحركة بعد ما التحميل يخلص، مش قبله — الكروت بتتحرك بأرقامها
            // النهائية جاهزة جواها، مش فاضية وبتتملى فجأة
            Loaded += async (_, _) =>
            {
                try
                {
                    await viewModel.LoadAsync();
                }
                catch (Exception ex)
                {
                    // التفاصيل الكاملة (stack trace) في crash.txt — الرسالة لوحدها مش كفاية لتتبع العطل
                    App.WriteCrashLog(ex);
                    Notify.Error("حصلت مشكلة أثناء تحميل الرئيسية: " + ex.Message);
                }
                finally
                {
                    // حتى لو التحميل فشل الأقسام لازم تظهر (بحالاتها الفاضية)،
                    // مش تفضل Opacity=0 وشاشة بيضا
                    PlayEntrance();
                }
            };
        }

        private FrameworkElement[] Sections =>
            [HeroSection, KpiSection, TilesSection, InsightsSection, ProductsSection, ActionSection];

        // ═══════════════════ الحركة ═══════════════════

        private void PlayEntrance()
        {
            var full = !s_entrancePlayed;
            s_entrancePlayed = true;

            var sections = Sections;
            for (var i = 0; i < sections.Length; i++)
            {
                // الدخول الكامل: قسم ورا التاني بفاصل 70ms وانزلاق 14px. الرجوع: ظهور 150ms بس
                var begin = TimeSpan.FromMilliseconds(full ? i * 70 : 0);
                var duration = TimeSpan.FromMilliseconds(full ? 280 : 150);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                sections[i].BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = ease });

                if (sections[i].RenderTransform is TranslateTransform slide)
                {
                    if (full)
                        slide.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(14, 0, duration) { BeginTime = begin, EasingFunction = ease });
                    else
                        slide.Y = 0;
                }
            }

            SetNumbers(animate: full);

            // نبضة الكأس مرتين بس لو فيه نجم فعلاً — محدودة، مش لوب لا نهائي
            // في شاشة المستخدم بيبص عليها كل يوم
            if (full && _viewModel.HasBestWorker && TrophyIcon.RenderTransform is ScaleTransform trophy)
            {
                var pulse = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(260))
                {
                    BeginTime = TimeSpan.FromMilliseconds(450), AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                trophy.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                trophy.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
        }

        /// <summary>
        /// الأرقام الكبيرة في كروت الأسبوع. العدّ لفوق بـDispatcherTimer مش
        /// Storyboard — الهدف TextBlock.Text (نص CLR)، مفيش DependencyProperty
        /// رقمي يتحرك عليه. 650ms وبيقف لوحده، فمابيشغلش الـUI thread بعد كده.
        /// </summary>
        private void SetNumbers(bool animate)
        {
            Show(TotalPiecesText, _viewModel.TotalPiecesThisWeek, v => v.ToString("N0"));
            Show(ActiveWorkersText, _viewModel.ActiveWorkersThisWeek, v => v.ToString("N0"));
            Show(NetWorkdaysText, (double)_viewModel.NetWorkdaysThisWeek, v => v.ToString("0.#"));
            Show(AbsencesText, _viewModel.UnexcusedAbsencesThisWeek, v => v.ToString("N0"));

            void Show(TextBlock target, double to, Func<double, string> format)
            {
                if (!animate || to == 0) { target.Text = format(to); return; }

                const int durationMs = 650;
                var start = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) =>
                {
                    var t = Math.Min(1.0, (DateTime.UtcNow - start).TotalMilliseconds / durationMs);
                    var eased = 1 - Math.Pow(1 - t, 3); // نفس منحنى CubicEase EaseOut
                    // الأعداد الصحيحة بتعدّ صحيح (مش 12.4 عامل) — اليوميات بس بكسور
                    var value = to * eased;
                    target.Text = format(to % 1 == 0 ? Math.Round(value) : value);
                    if (t >= 1.0) { timer.Stop(); target.Text = format(to); }
                };
                timer.Start();
            }
        }

        /// <summary>
        /// عند 900×560 (بعد طي الشريط) المحتوى ~800px: 4 كروت أرقام جنب بعض بتتزنق،
        /// والرسم جنب كروت الناس بيبقى أضيق من إنه يتقري — فبيتقسموا صفين
        /// </summary>
        private void ApplyResponsiveLayout(double width)
        {
            var narrow = width < NarrowWidth;

            KpiSection.Columns = narrow ? 2 : 4;
            // ProductsSection بقت 4 كروت (أكتر/أقل منتج، السلسلة، خطة الشهر) —
            // نفس مبدأ KpiSection بالظبط: عمودين عند 900px وإلا بتتزنق
            ProductsSection.Columns = narrow ? 2 : 4;

            PeopleColumn.Width = narrow ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
            Grid.SetColumn(PeoplePanel, narrow ? 0 : 1);
            Grid.SetRow(PeoplePanel, narrow ? 1 : 0);
        }

        // ═══════════════════ التنقل ═══════════════════

        private MainWindow? Main => Window.GetWindow(this) as MainWindow;

        private void Search_Click(object sender, RoutedEventArgs e) => Main?.OpenGlobalSearch();

        private void Pieces_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavEvaluationItem.IsChecked = true;
        }

        private void Workers_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavWorkersItem.IsChecked = true;
        }

        private void Products_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavProductsItem.IsChecked = true;
        }

        private void MonthlyPlan_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavMonthlyPlanItem.IsChecked = true;
        }

        private void DailyEntryTile_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavDailyEntryItem.IsChecked = true;
        }

        private void ReportsTile_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavReportsItem.IsChecked = true;
        }

        private void SettingsTile_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavSettingsItem.IsChecked = true;
        }

        private void Memory_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.NavMemoryItem.IsChecked = true;
        }

        /// <summary>الغياب والسلسلة → تبويب "الحضور والغياب" جوّه تسجيل الإنتاج اليومي</summary>
        private void Absences_Click(object sender, RoutedEventArgs e) =>
            Main?.OpenDailyEntryTab(MainWindow.DailyEntryAttendanceTab);

        /// <summary>أحسن/أقل عامل → ملفه، بنفس هبوط البحث السريع على عامل</summary>
        private async void Worker_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: HomeWorkerCard worker } || Main is not { } mw) return;

            try
            {
                await mw.LandOnWorkerAsync(worker.Name, worker.WorkerId);
            }
            catch (Exception ex)
            {
                Notify.Error("مقدرتش أفتح ملف العامل: " + ex.Message);
            }
        }

        /// <summary>أكتر/أقل منتج → صفحته، بنفس هبوط البحث السريع على منتج</summary>
        private void Product_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: HomeProductCard product } && Main is { } mw)
                mw.LandOnProduct(product.Name);
        }

        /// <summary>
        /// خطة في كارت الذاكرة → نفس ديالوج التذكير بتاع البداية بالظبط
        /// (App.ShowMemoryReminderAsync). "أجّل" بتغيّر الموعد فالكارت بيتحدّث؛
        /// "ابدأ الآن" بتنقل لتسجيل الإنتاج لوحدها.
        /// </summary>
        private async void MemoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: HomeMemoryItem item } || Window.GetWindow(this) is not { } owner)
                return;

            try
            {
                var choice = await App.ShowMemoryReminderAsync(owner, item.Memory);
                if (choice == MemoryReminderChoice.Postpone)
                {
                    await _viewModel.LoadAsync();
                    SetNumbers(animate: false);
                }
            }
            catch (Exception ex)
            {
                Notify.Error("حصلت مشكلة في خطة الذاكرة: " + ex.Message);
            }
        }
    }
}
