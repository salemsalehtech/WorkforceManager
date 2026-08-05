namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// مهارة عامل بنجومها وأداؤها المقاس — اللي بيتعرض في بروفايل العامل
    /// وقوايم اختيار العمال.
    ///
    /// بيحمل الرقمين مع بعض عن قصد: النجوم (رأي المدير) والأداء المقاس
    /// (رقم النظام). المدير لازم يشوف الاتنين عشان يعرف رأيه لسه مظبوط
    /// ولا الواقع اتغيّر.
    /// </summary>
    public class RankedWorkerDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public int StageId { get; init; }
        public string StageName { get; init; } = string.Empty;

        /// <summary>تقييم المدير من 1 لـ 5</summary>
        public int Stars { get; init; }

        /// <summary>الأداء الفعلي المقاس (1.0 = بيعمل الكوتة بالظبط)</summary>
        public decimal MeasuredRatio { get; init; }

        /// <summary>عدد أيام الشغل اللي القياس اتبنى عليها</summary>
        public int MeasuredDays { get; init; }

        public DateTime? MeasuredAt { get; init; }
        public DateTime? StarsUpdatedAt { get; init; }

        /// <summary>النجوم كنص للعرض ("★★★★☆")</summary>
        public string StarsText => new string('★', Stars) + new string('☆', 5 - Stars);

        /// <summary>فيه قياس فعلي ولا لسه؟</summary>
        public bool HasMeasurement => MeasuredAt is not null && MeasuredDays > 0;

        /// <summary>الأداء كنسبة مئوية ("115%")</summary>
        public string MeasuredText => HasMeasurement ? $"{MeasuredRatio * 100:0}%" : "—";

        /// <summary>
        /// شرح الرقم للمدير — من غيره الأرقام بتبقى بلا سياق.
        /// </summary>
        public string MeasuredTooltip => HasMeasurement
            ? $"إنتاجه الفعلي {MeasuredText} من الكوتة — محسوب من {MeasuredDays} يوم شغل"
            : "لسه مافيش إنتاج كفاية للقياس";
    }

    /// <summary>
    /// اقتراح تعديل تقييم: النجوم الحالية مقابل اللي أداؤه يستاهلها.
    ///
    /// اقتراح مش قرار — المدير هو اللي يوافق أو يتجاهل.
    /// </summary>
    public class SkillSuggestionDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public int StageId { get; init; }
        public string StageName { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;

        public int CurrentStars { get; init; }
        public int SuggestedStars { get; init; }

        public decimal MeasuredRatio { get; init; }
        public int MeasuredDays { get; init; }

        /// <summary>آخر مرة المدير عدّل التقييم (null = عمره ما اتعدّل)</summary>
        public DateTime? StarsUpdatedAt { get; init; }

        /// <summary>الاقتراح بيرفع ولا بينزّل</summary>
        public bool IsUpgrade => SuggestedStars > CurrentStars;

        public string CurrentStarsText => new string('★', CurrentStars) + new string('☆', 5 - CurrentStars);
        public string SuggestedStarsText => new string('★', SuggestedStars) + new string('☆', 5 - SuggestedStars);

        /// <summary>سبب الاقتراح بالعربي — المدير لازم يفهم الرقم جه منين</summary>
        public string Reason => IsUpgrade
            ? $"إنتاجه {MeasuredRatio * 100:0}% من الكوتة على مدار {MeasuredDays} يوم — أحسن من تقييمه الحالي"
            : $"إنتاجه {MeasuredRatio * 100:0}% من الكوتة على مدار {MeasuredDays} يوم — أقل من تقييمه الحالي";

        /// <summary>"من ★★★ لـ ★★★★" — الملخص اللي بيتعرض في السطر</summary>
        public string ChangeText => $"{CurrentStarsText}  ←  {SuggestedStarsText}";
    }

    /// <summary>نتيجة المراجعة الشهرية كاملة</summary>
    public class SkillReviewDto
    {
        public DateTime GeneratedAt { get; init; }

        public List<SkillSuggestionDto> Suggestions { get; init; } = new();

        public int UpgradeCount => Suggestions.Count(s => s.IsUpgrade);
        public int DowngradeCount => Suggestions.Count(s => !s.IsUpgrade);

        public bool HasSuggestions => Suggestions.Count > 0;

        /// <summary>ملخص للعرض في التنبيه</summary>
        public string SummaryText => Suggestions.Count == 0
            ? "كل التقييمات متطابقة مع الأداء الفعلي — مفيش حاجة محتاجة تعديل"
            : $"{UpgradeCount} عامل أداؤه بقى أحسن من تقييمه، و{DowngradeCount} أقل";
    }
}
