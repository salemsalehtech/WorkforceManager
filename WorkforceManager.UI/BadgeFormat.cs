namespace WorkforceManager.UI
{
    /// <summary>نص بادچ العدد المشترك بين كل بادچات الشريط الجانبي (النشاط، الذاكرة، الإشعارات)</summary>
    public static class BadgeFormat
    {
        public static string CountText(int count) => count > 99 ? "٩٩+" : count.ToString();
    }
}
