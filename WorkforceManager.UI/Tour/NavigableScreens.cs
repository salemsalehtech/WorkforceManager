namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// شاشة واحدة قابلة للوصول ليها مباشرة من "بحث سريع" بكتابة اسمها —
    /// نفس فكرة <see cref="SearchableSetting"/> (محتوى واجهة ثابت، مش صف
    /// قاعدة بيانات)، بس هدفها هنا الشاشة كلها مش عنصر جواها.
    /// </summary>
    public sealed class NavigableScreen
    {
        public required string Title { get; init; }

        /// <summary>
        /// x:Name زرار التنقل في MainWindow.xaml (مثلاً "NavWorkersItem") —
        /// **مش** اسم الـ View المعروض؛ فيه فرق موثّق (NavEvaluationItem
        /// بيعرض ReportsView، وNavReportsItem بيعرض ReportBuilderView).
        /// </summary>
        public required string NavItemName { get; init; }
    }

    /// <summary>
    /// الشاشات العشرة القابلة للتنقل من القايمة الجانبية — قايمة كاملة
    /// (مش انتقائية زي SearchableSettings) لأن "افتحلي شاشة كذا" لازم
    /// يشتغل لأي شاشة في البرنامج من غير استثناء.
    ///
    /// **صيانة**: شاشة جديدة في القايمة الجانبية محتاجة `RadioButton`
    /// جديد في MainWindow.xaml (زي أي شاشة تانية) زائد عنصر جديد هنا
    /// بنفس x:Name الزرار. نسيان الخطوة دي معناها الشاشة مش هتظهر في
    /// البحث، مش إنها هتكسر حاجة.
    /// </summary>
    public static class NavigableScreens
    {
        public static IReadOnlyList<NavigableScreen> Entries { get; } = new List<NavigableScreen>
        {
            new() { Title = "العمال", NavItemName = "NavWorkersItem" },
            new() { Title = "المنتجات والمراحل", NavItemName = "NavProductsItem" },
            new() { Title = "الخطة الشهرية", NavItemName = "NavMonthlyPlanItem" },
            new() { Title = "تسجيل الإنتاج اليومي", NavItemName = "NavDailyEntryItem" },
            new() { Title = "الذاكرة", NavItemName = "NavMemoryItem" },
            new() { Title = "التقييم والمتابعة", NavItemName = "NavEvaluationItem" },
            new() { Title = "التقارير", NavItemName = "NavReportsItem" },
            new() { Title = "سجل العمليات", NavItemName = "NavActivityLogItem" },
            new() { Title = "الإعدادات والنسخ", NavItemName = "NavSettingsItem" },
            new() { Title = "الحسابات الإدارية", NavItemName = "NavDepartmentAccountsItem" },
            new() { Title = "الدليل", NavItemName = "NavHelpItem" },
        };
    }
}
