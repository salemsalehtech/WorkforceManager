using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI
{
    /// <summary>
    /// النافذة الرئيسية: قائمة جانبية ثابتة + منطقة محتوى (MainContent)
    /// بتستبدل الـ View المعروض حسب الاختيار. كل شاشة بتتحل من الـ DI
    /// (Transient) — يعني كل تنقّل أو ضغطة على زرار القائمة بتبني الشاشة
    /// من جديد ببيانات طازة من قاعدة البيانات، فمفيش شاشة بتعرض أرقام قديمة.
    /// كل هاندلر متربط بحدثي Checked (تنقّل فعلي/كيبورد) وClick (إعادة
    /// تحميل عند الضغط على الشاشة المختارة أصلاً — Checked مبيتنفذش وقتها).
    /// أول Checked لزرار "العمال" بيحصل أثناء InitializeComponent قبل ما
    /// MainContent يتبني — الحارس (null check) بيتخطاه، والـ Constructor
    /// بيحمّل الشاشة الافتراضية بعدها.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CurrentUserContext _currentUser;

        public MainWindow(CurrentUserContext currentUser)
        {
            _currentUser = currentUser;

            InitializeComponent();

            // مكان الإشعارات بيتسجّل مرة واحدة هنا — بعدها أي شاشة
            // بتنادي Notify والإشعار بيوصل من غير ما تعرف مين بيعرضه
            Toasts.Register();

            // تاريخ اليوم بالعربي في بطاقة أسفل القائمة الجانبية
            TodayText.Text = DateTime.Today.ToString(
                "dddd d MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            ShowIdentity();
            RefreshActivityBadge();

            // شريط عنوان النافذة بيتلوّن بعد ما الـ Handle يتعمل — قبل
            // كده مفيش نافذة فعلية تتلوّن
            SourceInitialized += (_, _) => WindowChromeColors.Apply(this);

            // الشاشة الافتراضية عند فتح البرنامج: شاشة العمال
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
        }

        /// <summary>
        /// بيحدّث شارة "عمليات جديدة" على زرار سجل العمليات. بينادى بعد
        /// كل تنقّل — أرخص من مراقبة الداتابيز، واستعلام واحد مفهرس
        /// (CountSinceAsync) فمفيش تكلفة حقيقية تتحس. فتح شاشة السجل نفسها
        /// بيصفّر آخر وقت مشاهدة (ActivityLogViewModel.LoadAsync)، فالتنقّل
        /// اللي بعدها بيرجّع الشارة صفر.
        /// </summary>
        private async void RefreshActivityBadge()
        {
            using var scope = App.AppHost.Services.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<ActivityLogService>();
            var count = await log.GetUnseenCountAsync(_currentUser.AppUserId);

            ActivityBadgeText.Text = count > 99 ? "٩٩+" : count.ToString();
            ActivityBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// اسم المصنع والقسم في رأس القايمة الجانبية وفي عنوان النافذة.
        ///
        /// كان مكتوب "إدارة الإنتاج والأجور" — جملة بتوصف البرنامج
        /// للمستخدم اللي فاتح البرنامج. المكان ده يجاوب سؤال أنفع:
        /// النسخة دي بتاعة مين.
        /// </summary>
        public void ShowIdentity()
        {
            var settings = Data.AppSettingsStore.Load();

            SidebarLogo.Refresh();
            Views.AppIcon.ApplyTo(this);

            var factory = string.IsNullOrWhiteSpace(settings.FactoryName)
                ? "WMS"
                : settings.FactoryName!.Trim();

            var department = settings.DepartmentName?.Trim() ?? "";

            FactoryText.Text = factory;

            // اللاتيني بيتقلب في واجهة عربية لو اتساب على اتجاه الأب
            FactoryText.FlowDirection = HasArabic(factory)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

            DepartmentText.Text = department;
            DepartmentText.Visibility = department.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            Title = department.Length == 0 ? factory : $"{factory} — {department}";

            SignedInAsText.Text = _currentUser.ActorName;
        }

        /// <summary>
        /// خروج المستخدم الحالي وعرض شاشة الدخول تاني من غير ما البرنامج
        /// كله يقفل — مهم دلوقتي إن كل حساب إداري بقى ليه يوزر وباسورد
        /// لوحده، فأكتر من حد ممكن يستخدم نفس الجهاز في الشيفت.
        ///
        /// إلغاء شاشة الدخول (زرار الإغلاق) بعد الخروج معناه المستخدم
        /// مش عايز يدخل بحساب تاني دلوقتي — نفس قاعدة الإغلاق وقت فتح
        /// البرنامج (LoginWindow.Close_Click)، فالبرنامج بيقفل بدل ما
        /// يفضل واقف من غير حد داخل بيه.
        /// </summary>
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            if (!Notify.Ask("تسجيل الخروج من الحساب الحالي؟", "تأكيد")) return;

            _currentUser.SignOut();

            var login = new LoginWindow();
            if (login.ShowDialog() != true)
            {
                Application.Current.Shutdown();
                return;
            }

            ShowIdentity();
            RefreshActivityBadge(); // الحساب الجديد ممكن يكون له عدد مختلف تمامًا

            // الرجوع للشاشة الافتراضية بدل ما يفضل واقف على شاشة ممكن
            // ماعادش مسموح للحساب الجديد يشوفها (زي الحسابات الإدارية)
            NavWorkersItem.IsChecked = true;
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
        }

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        private void NavWorkers_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
            RefreshActivityBadge();
        }

        private void NavProducts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ProductsView>();
            RefreshActivityBadge();
        }

        private void NavDailyEntry_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<DailyEntryView>();
            RefreshActivityBadge();
        }

        private void NavEvaluation_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportsView>();
            RefreshActivityBadge();
        }

        private void NavReports_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportBuilderView>();
            RefreshActivityBadge();
        }

        private void NavActivityLog_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ActivityLogView>();
            // فتح الشاشة بيصفّر آخر وقت مشاهدة جوه الـ ViewModel نفسها؛
            // الرجوع هنا بعد شوية (تنقّل تاني) هو اللي بيعرض الصفر فعليًا
            RefreshActivityBadge();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<SettingsView>();
            RefreshActivityBadge();
        }

        private void NavDepartmentAccounts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<DepartmentAccountsView>();
            RefreshActivityBadge();
        }
    }
}
