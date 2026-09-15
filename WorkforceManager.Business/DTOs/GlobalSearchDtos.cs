namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// فئات البحث الشامل. الثمانية الأولى (Worker...DepartmentAccount)
    /// مرتبطة بقاعدة بيانات وبتتحمّل من <see cref="Services.GlobalSearchService"/>؛
    /// الباقي (Setting، HelpTopic، IntentAnswer، Screen) محتوى واجهة ثابت
    /// أو محسوب — الواجهة هي اللي بتضيفهم لنفس القايمة الموحّدة بعد ما
    /// الخدمة ترجّع الفئات المرتبطة بقاعدة البيانات.
    /// </summary>
    public enum SearchCategory
    {
        Worker,
        Product,
        ProductionStage,
        InitialBalance,
        MemoryPlan,
        ActivityLogEntry,
        ReportTemplate,
        DepartmentAccount,
        Setting,
        HelpTopic,

        /// <summary>
        /// إجابة محسوبة فورية بالنية (شوف <see cref="Services.SearchIntentService"/>)،
        /// مش صف قاعدة بيانات — زي Setting/HelpTopic بالظبط في المبدأ، بس
        /// بتتضاف من MainWindow مش من GlobalSearchService لأنها محتاجة
        /// تفكيك النية الأول. دايمًا نتيجة واحدة بس على رأس القايمة لو
        /// اتطابقت (شوف Score في MainWindow.SearchAllCategoriesAsync).
        /// </summary>
        IntentAnswer,

        /// <summary>
        /// تنقّل مباشر لشاشة كاملة بالاسم (زي "التقارير")، مش عنصر معيّن
        /// جواها — محتوى واجهة ثابت زي Setting/HelpTopic (شوف
        /// NavigableScreens في مشروع الواجهة)، مش صف قاعدة بيانات.
        /// </summary>
        Screen
    }

    /// <summary>
    /// نتيجة بحث واحدة، بشكل موحّد عبر كل الفئات. بتحمل بس المعرّفات
    /// (IDs) اللازمة للواجهة إنها "تهبط" على العنصر بالظبط بعد التنقّل —
    /// مش الكيان الكامل، عشان الخدمة تفضل بسيطة ومتكررش بيانات محمّلة
    /// أصلًا في شاشة الهبوط نفسها. كل فئة بتملي المعرّفات اللي تخصّها بس
    /// (الباقي null)؛ الواجهة هي اللي بتفسّرهم حسب <see cref="Category"/>.
    /// </summary>
    public class GlobalSearchResult
    {
        public required SearchCategory Category { get; init; }

        /// <summary>النص الأساسي المعروض (اسم العامل/المنتج/المرحلة/عنوان الموضوع...)</summary>
        public required string PrimaryText { get; init; }

        /// <summary>سياق إضافي اختياري (مثلاً اسم المنتج الأب لمرحلة، أو تاريخ حدث سجل)</summary>
        public string? SecondaryText { get; init; }

        /// <summary>
        /// كل ما زاد، كان التطابق أدق — نفس Score اللي رجّعه SearchMatcher.
        /// set مش init عن قصد: MainWindow.SearchAllCategoriesAsync بيزوّدها
        /// بترقية "الترتيب بالاستخدام" (شوف SearchRankingScorer) بعد ما
        /// النتيجة تتبني، قبل الفرز النهائي.
        /// </summary>
        public required int Score { get; set; }

        public int? WorkerId { get; init; }
        public int? ProductId { get; init; }
        public int? ProductionStageId { get; init; }
        public int? InitialBalanceId { get; init; }
        public int? MemoryPlanId { get; init; }
        public int? ActivityEventId { get; init; }

        /// <summary>لازم لهبوط سجل العمليات — بيوسّع مدى التاريخ المعروض ليغطّي الحدث ده</summary>
        public DateTime? ActivityEventOccurredAt { get; init; }

        /// <summary>قوالب التقارير متعرّفة بالاسم، مش رقم — نفس مفتاح ReportTemplateStore</summary>
        public string? ReportTemplateName { get; init; }

        /// <summary>
        /// **الاتنين دول بس بتتملى من الواجهة، مش من GlobalSearchService** —
        /// فئتي الإعدادات والدليل محتوى ثابت في الواجهة (HelpTopics/HelpFaq/
        /// SearchableSettings)، مش صفوف قاعدة بيانات، فالخدمة نفسها ماعندهاش
        /// داعي تعرف عنهم حاجة. موجودين هنا (مش في نوع واجهة منفصل) عشان
        /// GlobalSearchResult يفضل الشكل الوحيد اللي بيتحرك من المطابقة
        /// للعرض للهبوط، بدل نوع تغليف تاني يكرر نفس الحقول.
        /// </summary>
        public string? SettingTargetElementName { get; init; }

        /// <summary>true لسؤال في الأسئلة الشائعة، false (الافتراضي) لموضوع دليل عادي — يفرّق شكل الهبوط بس</summary>
        public bool IsFaqEntry { get; init; }

        /// <summary>
        /// الإجابة المحسوبة نفسها — بس لما Category == IntentAnswer. WorkerId/
        /// ProductId فوق بيتملوا عادي كمان في الحالة دي (من SearchIntentAnswer)
        /// عشان الهبوط عند الدوسة يشتغل بنفس منطق فئتي Worker/Product الموجود.
        /// </summary>
        public SearchIntentAnswer? IntentAnswer { get; init; }

        /// <summary>x:Name زرار التنقل في MainWindow.xaml — بس لما Category == Screen، شوف NavigableScreens</summary>
        public string? NavItemName { get; init; }
    }
}
