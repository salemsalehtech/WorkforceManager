using WorkforceManager.Data;

namespace WorkforceManager.UI
{
    /// <summary>
    /// قاعدة الطي التلقائي للشريط الجانبي بعد التنقّل منه — منفصلة عن
    /// MainWindow عشان تتختبر من غير نافذة (SidebarNavigationRuleTests).
    /// </summary>
    public static class SidebarNavigationRule
    {
        /// <summary>
        /// الدوسة على أي بند في القايمة بتطوي الشريط، كل مرة، حتى لو المستخدم
        /// لسه فاتحه بإيده — ماعدا الرئيسية (ليها قاعدتها الخاصة، ApplyHomeSidebarRule).
        /// ولو مطوي أصلًا مفيش حاجة، عشان مايحصلش أي وميض.
        /// </summary>
        public static bool ShouldAutoCollapse(bool targetIsHome, bool isCollapsed) => !targetIsHome && !isCollapsed;

        /// <summary>
        /// مسار الكتابة الوحيد لعلم SidebarCollapsed المحفوظ — زرار الطي والطي
        /// التلقائي الاتنين بيعدّوا من هنا، فمفيش علم تاني منافس.
        /// </summary>
        public static void PersistCollapsed(bool collapsed, string? settingsPath = null)
        {
            var settings = AppSettingsStore.Load(settingsPath);
            settings.SidebarCollapsed = collapsed;
            AppSettingsStore.Save(settings, settingsPath);
        }
    }
}
