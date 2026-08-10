using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
        public MainWindow()
        {
            InitializeComponent();

            // مكان الإشعارات بيتسجّل مرة واحدة هنا — بعدها أي شاشة
            // بتنادي Notify والإشعار بيوصل من غير ما تعرف مين بيعرضه
            Toasts.Register();

            // تاريخ اليوم بالعربي في بطاقة أسفل القائمة الجانبية
            TodayText.Text = DateTime.Today.ToString(
                "dddd d MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            ShowIdentity();

            // شريط عنوان النافذة بيتلوّن بعد ما الـ Handle يتعمل — قبل
            // كده مفيش نافذة فعلية تتلوّن
            SourceInitialized += (_, _) => WindowChromeColors.Apply(this);

            // الشاشة الافتراضية عند فتح البرنامج: شاشة العمال
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
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
        }

        private static bool HasArabic(string text) =>
            text.Any(c => c >= '؀' && c <= 'ۿ');

        private void NavWorkers_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return; // بيحصل مرة واحدة أثناء تهيئة النافذة
            MainContent.Content = App.AppHost.Services.GetRequiredService<WorkersView>();
        }

        private void NavProducts_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ProductsView>();
        }

        private void NavDailyEntry_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<DailyEntryView>();
        }

        private void NavEvaluation_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportsView>();
        }

        private void NavReports_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ReportBuilderView>();
        }

        private void NavActivityLog_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<ActivityLogView>();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContent is null) return;
            MainContent.Content = App.AppHost.Services.GetRequiredService<SettingsView>();
        }
    }
}
