using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// عامل مرتّب بتقييمه على مرحلة — اللي بيتعرض في قوايم اختيار العمال.
    ///
    /// بيحمل مصدر التقييم وحجم العينة مع الرقم عن قصد: المستخدم لازم
    /// يعرف الرقم ده جه منين قبل ما يبني عليه قرار، خصوصًا لما النظام
    /// يكون غيّر تقدير بشري.
    /// </summary>
    public class RankedWorkerDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        /// <summary>نسبة الأداء (1.0 = بيعمل الكوتة بالظبط)</summary>
        public decimal RatingValue { get; init; }

        public SkillLevel Level { get; init; }

        /// <summary>يدوي ولا محسوب من الإنتاج</summary>
        public SkillRatingSource Source { get; init; }

        /// <summary>عدد أيام الشغل اللي الحساب التلقائي اتبنى عليها (0 لو يدوي)</summary>
        public int SampleDays { get; init; }

        /// <summary>آخر تقدير بشري — بيفضل معروض حتى بعد الحساب التلقائي</summary>
        public decimal? LastManualValue { get; init; }

        /// <summary>النسبة كنص مئوي للعرض ("115%")</summary>
        public string RatingText => $"{RatingValue * 100:0}%";

        /// <summary>
        /// شرح مصدر الرقم للمستخدم — ده اللي بيمنع إحساس "الرقم اتغيّر لوحده".
        /// </summary>
        public string SourceText => Source == SkillRatingSource.Auto
            ? $"محسوب من إنتاج {SampleDays} يوم"
            : "تقدير يدوي";

        /// <summary>النظام غيّر تقدير بشري؟ (الواجهة بتعرض الاتنين ساعتها)</summary>
        public bool OverridesManualValue =>
            Source == SkillRatingSource.Auto && LastManualValue is not null;
    }
}
