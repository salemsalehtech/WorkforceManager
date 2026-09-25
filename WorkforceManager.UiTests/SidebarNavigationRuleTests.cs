using System.IO;
using WorkforceManager.Data;
using WorkforceManager.UI;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// الطي التلقائي للشريط بعد التنقّل منه — القرار (ShouldAutoCollapse) والحفظ
    /// (PersistCollapsed) في ملف إعدادات مؤقت، مش ملف المستخدم الحقيقي.
    /// </summary>
    public class SidebarNavigationRuleTests : IDisposable
    {
        private readonly string _settingsPath =
            Path.Combine(Path.GetTempPath(), $"wfm-sidebar-{Guid.NewGuid():N}", "settings.json");

        public void Dispose()
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        /// <summary>نفس اللي NavItem_Click بيعمله: القرار، ولو أيوه يكتب نفس العلم المحفوظ</summary>
        private void Navigate(bool targetIsHome, bool isCollapsed)
        {
            if (SidebarNavigationRule.ShouldAutoCollapse(targetIsHome, isCollapsed))
                SidebarNavigationRule.PersistCollapsed(true, _settingsPath);
        }

        [Fact]
        public void NavigatingToNonHomeScreen_PersistsCollapsed_KeepingOtherSettings()
        {
            AppSettingsStore.Save(new AppSettings { SidebarCollapsed = false, DarkMode = true }, _settingsPath);

            Navigate(targetIsHome: false, isCollapsed: false);

            var saved = AppSettingsStore.Load(_settingsPath);
            Assert.True(saved.SidebarCollapsed);
            Assert.True(saved.DarkMode);
        }

        [Fact]
        public void NavigatingToHome_DoesNotForceCollapseIntoPersistedState()
        {
            // الرئيسية بتطوي الشريط بقاعدتها الخاصة من غير حفظ (persist: false)،
            // فالتنقّل ليها مايغيّرش التفضيل المحفوظ "مفتوح"
            AppSettingsStore.Save(new AppSettings { SidebarCollapsed = false }, _settingsPath);

            Navigate(targetIsHome: true, isCollapsed: false);

            Assert.False(AppSettingsStore.Load(_settingsPath).SidebarCollapsed);
        }

        [Fact]
        public void AlreadyCollapsed_NoCollapseAgain()
        {
            Assert.False(SidebarNavigationRule.ShouldAutoCollapse(targetIsHome: false, isCollapsed: true));
        }

        [Fact]
        public void ReopenedByHand_NextNavigationCollapsesAgain()
        {
            // مفيش استثناء لـ"لسه فاتحه": أي تنقّل وهو مفتوح بيطويه
            AppSettingsStore.Save(new AppSettings { SidebarCollapsed = true }, _settingsPath);
            SidebarNavigationRule.PersistCollapsed(false, _settingsPath); // فتح يدوي (زرار الطي)

            Navigate(targetIsHome: false, isCollapsed: false);

            Assert.True(AppSettingsStore.Load(_settingsPath).SidebarCollapsed);
        }
    }
}
