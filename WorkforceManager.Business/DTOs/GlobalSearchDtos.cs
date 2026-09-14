namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// الفئات العشرة اللي البحث الشامل بيغطّيها. آخر اتنين (Setting و
    /// HelpTopic) محتوى ثابت في الواجهة (مش صفوف قاعدة بيانات)، فمش بيتم
    /// تحميلهم من <see cref="Services.GlobalSearchService"/> — الواجهة هي
    /// اللي بتضيفهم لنفس القايمة الموحّدة بعد ما الخدمة ترجّع الفئات
    /// السبعة المرتبطة بقاعدة البيانات.
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
        HelpTopic
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

        /// <summary>كل ما زاد، كان التطابق أدق — نفس Score اللي رجّعه SearchMatcher</summary>
        public required int Score { get; init; }

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
    }
}
