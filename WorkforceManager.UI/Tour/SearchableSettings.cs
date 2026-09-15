namespace WorkforceManager.UI.Tour
{
    /// <summary>
    /// عنصر إعدادات واحد قابل للبحث. الإعدادات مش صفوف قاعدة بيانات
    /// (JSON بجانب قاعدة البيانات، شوف AppSettingsStore) فمفيش استعلام
    /// يرجّعها — القايمة هنا محتوى ثابت مكتوب باليد، بنفس فكرة
    /// <see cref="HelpTopics"/>/<see cref="LearnFeaturesContent"/>.
    /// </summary>
    public sealed class SearchableSetting
    {
        public required string Title { get; init; }
        public required string Description { get; init; }

        /// <summary>x:Name العنصر المستهدف في SettingsView.xaml — نفس الأسماء اللي جولة "الدليل" أضافتها.</summary>
        public required string TargetElementName { get; init; }
    }

    /// <summary>
    /// كل بند إعدادات يستاهل يوصله البحث الشامل. **مش قايمة كل عنصر في
    /// الشاشة** — بس البنود اللي المستخدم واقعي يدوّر عليها بالاسم
    /// (زي "كلمة سر العمليات" أو "الوضع الليلي")، مش كل TextBox فرعي.
    ///
    /// **صيانة**: بند إعدادات جديد محتاج x:Name جديد على العنصر الثابت
    /// في SettingsView.xaml (أبدًا جوّه DataTemplate — نفس القاعدة في كل
    /// مكان تاني بيستخدم FindName) زائد عنصر جديد هنا. نسيان الخطوة دي
    /// معناه البند مش هيظهر في البحث، مش إنه هيكسر حاجة.
    /// </summary>
    public static class SearchableSettings
    {
        public static IReadOnlyList<SearchableSetting> Entries { get; } = new List<SearchableSetting>
        {
            new()
            {
                Title = "كلمة سر العمليات",
                Description = "الكلمة اللي البرنامج بيطلبها قبل عمليات حسّاسة زي حذف عامل أو تعديل أجر",
                TargetElementName = "SetPasswordButton"
            },
            new()
            {
                Title = "النسخ الاحتياطي الخارجي",
                Description = "نسخة يومية تلقائية لقاعدة البيانات على فلاشة أو قرص تاني — الحماية الوحيدة لو الهارد نفسه باظ",
                TargetElementName = "ChooseExternalFolderButton"
            },
            new()
            {
                Title = "النسخ الاحتياطي المحلي",
                Description = "نسخة تلقائية كل يوم أول ما البرنامج يفتح، وزرار لأخذ نسخة يدوية فورية",
                TargetElementName = "BackupCard"
            },
            new()
            {
                Title = "أسباب الهالك",
                Description = "القايمة اللي بتظهر وقت تسجيل الهالك — تقرير الهالك بيتجمّع بيها",
                TargetElementName = "ScrapReasonsCard"
            },
            new()
            {
                Title = "هوية التقارير المصدَّرة",
                Description = "اسم المصنع والقسم وشعار التقارير اللي بيتكتبوا فوق كل ملف Excel مُصدَّر",
                TargetElementName = "ReportIdentityCard"
            },
            new()
            {
                Title = "شعار البرنامج",
                Description = "الشعار اللي بيحل محل شعار WMS في القايمة الجانبية وشاشة الدخول",
                TargetElementName = "AppLogoCard"
            },
            new()
            {
                Title = "الوضع الليلي",
                Description = "ألوان غامقة أريح للعين — بيتفعّل بعد إعادة تشغيل البرنامج",
                TargetElementName = "DarkModeCard"
            },
            new()
            {
                Title = "تنظيف سجل العمليات",
                Description = "مدة الاحتفاظ بسطور سجل العمليات قبل ما تتمسح لوحدها",
                TargetElementName = "LogRetentionCard"
            },
            new()
            {
                Title = "استرجاع نسخة احتياطية",
                Description = "الرجوع لنسخة سابقة من قاعدة البيانات لو حصلت مشكلة كبيرة",
                TargetElementName = "RestoreBackupButton"
            },
            new()
            {
                Title = "الإصدار ومكان البيانات",
                Description = "رقم إصدار البرنامج، تاريخ الإصدار، ومكان حفظ قاعدة البيانات على الجهاز",
                TargetElementName = "AppInfoFooter"
            }
        };
    }
}
