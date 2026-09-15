using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// بس الأجزاء اللي ميزة الشريط الجانبي القابل للطي محتاجاها — نفس نمط
    /// ReportTemplateTests/SearchRankingTests بالظبط (ملف مؤقت معزول لكل
    /// اختبار، بيتشال بعد كل اختبار).
    /// </summary>
    public class AppSettingsStoreTests : IDisposable
    {
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), $"wm-settings-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ملف مؤقت */ }
        }

        [Fact]
        public void Missing_settings_file_defaults_to_sidebar_expanded()
        {
            var settings = AppSettingsStore.Load(_path);

            Assert.False(settings.SidebarCollapsed);
        }

        [Fact]
        public void Sidebar_collapsed_state_round_trips_through_save_and_load()
        {
            var settings = AppSettingsStore.Load(_path);
            settings.SidebarCollapsed = true;
            AppSettingsStore.Save(settings, _path);

            var reloaded = AppSettingsStore.Load(_path);

            Assert.True(reloaded.SidebarCollapsed);
        }

        [Fact]
        public void Corrupt_settings_file_returns_defaults_instead_of_crashing()
        {
            File.WriteAllText(_path, "{ not valid json");

            var settings = AppSettingsStore.Load(_path);

            Assert.False(settings.SidebarCollapsed);
        }
    }
}
