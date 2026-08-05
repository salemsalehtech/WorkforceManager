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
    /// سطر في المراجعة الشهرية: النجوم الحالية مقابل اللي أداؤه يستاهلها.
    ///
    /// اقتراح مش قرار — المدير هو اللي يوافق أو يتجاهل.
    ///
    /// السطر بياخد واحدة من تلات حالات: ارفع، نزّل، أو **أكّد** (النجوم
    /// مظبوطة بس المدير عمره ما قالها بنفسه — القيمة اللي عليها حطها
    /// الترحيل).
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

        /// <summary>المدير عمره ما قال رأيه في المهارة دي</summary>
        public bool IsUnrated => StarsUpdatedAt is null;

        /// <summary>
        /// مفيش تعديل مطلوب — التقييم مطابق للأداء، بس لسه مبدئي ومستني
        /// تأكيد المدير. الحالة دي مبتحصلش غير للمهارات اللي عمرها ما
        /// اتقيّمت بإيد.
        /// </summary>
        public bool IsConfirmation => SuggestedStars == CurrentStars;

        /// <summary>الاقتراح بيرفع التقييم</summary>
        public bool IsUpgrade => SuggestedStars > CurrentStars;

        /// <summary>الاقتراح بينزّل التقييم</summary>
        public bool IsDowngrade => SuggestedStars < CurrentStars;

        public string CurrentStarsText => new string('★', CurrentStars) + new string('☆', 5 - CurrentStars);
        public string SuggestedStarsText => new string('★', SuggestedStars) + new string('☆', 5 - SuggestedStars);

        /// <summary>الأداء المقاس كجملة — مشتركة بين كل الأسباب</summary>
        private string MeasuredPhrase =>
            $"إنتاجه {MeasuredRatio * 100:0}% من الكوتة على مدار {MeasuredDays} يوم";

        /// <summary>سبب الاقتراح بالعربي — المدير لازم يفهم الرقم جه منين</summary>
        public string Reason => IsConfirmation
            ? $"{MeasuredPhrase} — مطابق للتقييم اللي عليه، بس التقييم ده مبدئي. أكّده عشان يبقى رأيك انت."
            : IsUpgrade
                ? $"{MeasuredPhrase} — أحسن من تقييمه الحالي"
                : $"{MeasuredPhrase} — أقل من تقييمه الحالي";

        /// <summary>"★★★ ← ★★★★" — الملخص اللي بيتعرض في السطر</summary>
        public string ChangeText => IsConfirmation
            ? SuggestedStarsText
            : $"{CurrentStarsText}  ←  {SuggestedStarsText}";
    }

    /// <summary>نتيجة المراجعة الشهرية كاملة</summary>
    public class SkillReviewDto
    {
        public DateTime GeneratedAt { get; init; }

        public List<SkillSuggestionDto> Suggestions { get; init; } = new();

        public int UpgradeCount => Suggestions.Count(s => s.IsUpgrade);
        public int DowngradeCount => Suggestions.Count(s => s.IsDowngrade);
        public int ConfirmationCount => Suggestions.Count(s => s.IsConfirmation);

        public bool HasSuggestions => Suggestions.Count > 0;

        /// <summary>
        /// ملخص للعرض في التنبيه. بيتبني من الحالات الموجودة فعلًا بس —
        /// "0 عامل أداؤه بقى أحسن" جملة بتشغّل دماغ المدير من غير داعي.
        /// </summary>
        public string SummaryText
        {
            get
            {
                if (Suggestions.Count == 0)
                    return "كل التقييمات متطابقة مع الأداء الفعلي — مفيش حاجة محتاجة تعديل";

                var parts = new List<string>();
                if (UpgradeCount > 0) parts.Add($"{UpgradeCount} أداؤه بقى أحسن من تقييمه");
                if (DowngradeCount > 0) parts.Add($"{DowngradeCount} أقل من تقييمه");
                if (ConfirmationCount > 0) parts.Add($"{ConfirmationCount} تقييمه مبدئي ومستني تأكيدك");

                return string.Join("، و", parts);
            }
        }
    }
}
