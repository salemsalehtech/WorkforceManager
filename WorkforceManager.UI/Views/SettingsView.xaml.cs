using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة الإعدادات: الكود هنا شكلي بس (ربط الـ ViewModel + تبديل القسم الظاهر) —
    /// كل المنطق في SettingsViewModel حسب نمط MVVM المتبع في المشروع.
    /// "الحسابات الإدارية" و"الدليل" أقسام فيها بتستضيف شاشاتهم الأصلية (IEmbeddedScreenHost).
    /// </summary>
    public partial class SettingsView : UserControl, IEmbeddedScreenHost
    {
        /// <summary>أقسام الإعدادات — القيم هي نفس Tag بتاع عناصر SectionList في الـXAML</summary>
        public const string SecuritySection = "Security";
        public const string AccountsSection = "Accounts";
        public const string BackupSection = "Backup";
        public const string OperationsSection = "Operations";
        public const string IdentitySection = "Identity";
        public const string GuideSection = "Guide";
        public const string AboutSection = "About";

        /// <summary>
        /// كل عنصر مستهدف بالبحث السريع/الجولة (x:Name في الـXAML) ← القسم اللي فيه.
        /// من غيرها BringIntoView على عنصر في قسم مخفي مابيعملش حاجة.
        /// </summary>
        private static readonly Dictionary<string, string> SectionOfElement = new()
        {
            ["SetPasswordButton"] = SecuritySection,
            ["ChooseExternalFolderButton"] = BackupSection,
            ["BackupCard"] = BackupSection,
            ["RestoreBackupButton"] = BackupSection,
            ["ScrapReasonsCard"] = OperationsSection,
            ["LogRetentionCard"] = OperationsSection,
            ["ReportIdentityCard"] = IdentitySection,
            ["AppLogoCard"] = IdentitySection,
            ["DarkModeCard"] = IdentitySection,
            ["AppInfoFooter"] = AboutSection,
        };

        private static readonly (string Key, string Title, string Description, MaterialDesignThemes.Wpf.PackIconKind Icon)[] Sections =
        {
            (SecuritySection, "الحماية", "كلمة سر العمليات اللي بتحمي الحذف وتعديل الأجور", MaterialDesignThemes.Wpf.PackIconKind.ShieldLockOutline),
            (AccountsSection, "الحسابات الإدارية", "حسابات المديرين ورؤساء الأقسام وصلاحياتهم", MaterialDesignThemes.Wpf.PackIconKind.AccountTie),
            (BackupSection, "النسخ الاحتياطي", "نسخ خارجي ومحلي واسترجاع نسخة سابقة", MaterialDesignThemes.Wpf.PackIconKind.BackupRestore),
            (OperationsSection, "إعدادات الشغل", "أسباب الهالك ومدة الاحتفاظ بسجل العمليات", MaterialDesignThemes.Wpf.PackIconKind.Tune),
            (IdentitySection, "الهوية والمظهر", "اسم المصنع والشعار والوضع الليلي", MaterialDesignThemes.Wpf.PackIconKind.PaletteOutline),
            (GuideSection, "الدليل", "شرح كل شاشة وجولات تعليمية قصيرة", MaterialDesignThemes.Wpf.PackIconKind.HelpCircleOutline),
            (AboutSection, "حول البرنامج", "رقم الإصدار ومكان حفظ البيانات", MaterialDesignThemes.Wpf.PackIconKind.InformationOutline),
        };

        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Landing.Setup("الإعدادات", "اختار القسم اللي عايز تظبطه",
                Sections.Select(s => new HubCardInfo { Key = s.Key, Title = s.Title, Description = s.Description, Icon = s.Icon }).ToList());
            Landing.CardChosen += card => SelectSection(card.Key);
            PageBar.BackRequested += ShowLanding;

            // تحديث المعلومات المعروضة أول ما الشاشة تظهر
            Loaded += (_, _) => EntranceAnimation.PlayFadeSlideIn(this);
            Loaded += async (_, _) =>
            {
                viewModel.LoadInfo();                        // ملفات النسخ
                await viewModel.LoadOperationsStateAsync();  // حالة كلمة السر (قاعدة بيانات)
            };
        }

        private void ShowLanding()
        {
            PagesLayer.Visibility = Visibility.Collapsed;
            Landing.Visibility = Visibility.Visible;
        }

        private void ApplySection(string section)
        {
            SecurityPanel.Visibility = Visible(section == SecuritySection);
            BackupPanel.Visibility = Visible(section == BackupSection);
            OperationsPanel.Visibility = Visible(section == OperationsSection);
            IdentityPanel.Visibility = Visible(section == IdentitySection);
            AboutPanel.Visibility = Visible(section == AboutSection);

            // الشاشتين المتداخلتين بيتبنوا أول ما يظهروا (Loaded بتاع LazyScreenHost)
            var plain = section is not (AccountsSection or GuideSection);
            SettingsScroll.Visibility = Visible(plain);
            AccountsHost.Visibility = Visible(section == AccountsSection);
            GuideHost.Visibility = Visible(section == GuideSection);

            Landing.Visibility = Visibility.Collapsed;
            PagesLayer.Visibility = Visibility.Visible;
            PageBar.Set("الإعدادات", Sections.First(s => s.Key == section).Title);
        }

        private static Visibility Visible(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>يفتح القسم اللي فيه عنصر بالاسم (x:Name) — قبل FindName/BringIntoView في البحث السريع والجولة</summary>
        public void ShowSectionOf(string elementName)
        {
            if (SectionOfElement.TryGetValue(elementName, out var section)) SelectSection(section);
        }

        public void SelectSection(string section)
        {
            if (Sections.Any(s => s.Key == section)) ApplySection(section);
        }

        // ===== IEmbeddedScreenHost =====

        public object? Show(Type screenType)
        {
            if (screenType == typeof(DepartmentAccountsView))
            {
                SelectSection(AccountsSection);
                return AccountsHost.EnsureCreated();
            }

            if (screenType == typeof(HelpView))
            {
                SelectSection(GuideSection);
                return GuideHost.EnsureCreated();
            }

            return null;
        }

        public object? Find(Type screenType) =>
            screenType == typeof(DepartmentAccountsView) ? AccountsHost.Content :
            screenType == typeof(HelpView) ? GuideHost.Content : null;

        public FrameworkElement? FindElement(string name)
        {
            if (FindName(name) is FrameworkElement own) { ShowSectionOf(name); return own; }

            return (AccountsHost.Content as FrameworkElement)?.FindName(name) as FrameworkElement
                ?? (GuideHost.Content as FrameworkElement)?.FindName(name) as FrameworkElement;
        }
    }
}
