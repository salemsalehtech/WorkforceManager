namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// صيغة "الترتيب بالاستخدام" لـ"بحث سريع": استعلام مشابه اتختار له نفس
    /// النتيجة قبل كده بياخد ترقية درجة صغيرة تقدّمه على نتايج تانية قريبة
    /// منه في الدرجة — مش نموذج تعلّم آلي، مجرد عدّاد + تراجع زمني بسيط
    /// ومفسَّر بالكامل، شوف <see cref="ComputeBoost"/>.
    ///
    /// عايشة في Core جنب <see cref="SearchMatcher"/> بالظبط لنفس السبب:
    /// منطق نقي بدون DB/UI، قابل للاختبار مباشرة بمعامل "دلوقتي" صريح بدل
    /// الاعتماد على DateTime.Now الحقيقي وقت الاختبار.
    /// </summary>
    public static class SearchRankingScorer
    {
        /// <summary>
        /// أكتر عدد اختيارات بيتحسب في الترقية — بعدها الفايدة الإضافية
        /// ضئيلة، ومفيش داعي استعلام اتختار 50 مرة ياخد ترقية أكبر من
        /// استعلام اتختار 5 مرات.
        /// </summary>
        public const int MaxRankingPickCount = 5;

        /// <summary>نقاط الترقية لكل اختيار واحد (قبل التراجع الزمني والحد الأقصى)</summary>
        public const int RankingPickWeight = 30;

        /// <summary>
        /// الحد الأقصى المطلق للترقية — أقل بكتير من الفرق بين درجتي تصنيف
        /// متجاورتين في SearchMatcher (200: Exact=1000 لـ Prefix=800)، عشان
        /// الترقية تقدر تقدّم نتيجة على تانية قريبة منها في الدرجة، لكن
        /// **مستحيل** تخلّي نتيجة Fuzzy ضعيفة تقفز فوق تطابق دقيق.
        /// </summary>
        public const int MaxRankingBoost = 150;

        /// <summary>عدد الأيام اللي وزن الاختيار بيتنصّف بعدها — "نص القوة كل 30 يوم"</summary>
        public const int RecencyHalfLifeDays = 30;

        /// <summary>
        /// الترقية المضافة لدرجة نتيجة بحث اتختارت قبل كده لاستعلام مشابه.
        ///
        /// اختيار قديم واحد (حتى لو اتكرر كتير قبل كده) بيدوب وزنه بالتراجع
        /// الزمني — فمجموعة اختيارات حديثة مختلفة (كل واحدة PickCount=1 بس
        /// عامل تراجعها قريب من 1) بتجمع ترقية أعلى من اختيار واحد قديم
        /// عدد مرّاته كبير، وده بالظبط المطلوب: النتيجة تتبع الاستخدام
        /// الحديث، مش تتجمّد على أول اختيار حصل.
        /// </summary>
        public static int ComputeBoost(int pickCount, DateTime lastPickedAt, DateTime now)
        {
            if (pickCount <= 0) return 0;

            var ageDays = Math.Max(0, (now.Date - lastPickedAt.Date).Days);
            var recencyFactor = Math.Pow(0.5, ageDays / (double)RecencyHalfLifeDays);

            var boost = Math.Min(pickCount, MaxRankingPickCount) * RankingPickWeight * recencyFactor;

            return Math.Min(MaxRankingBoost, (int)Math.Round(boost));
        }
    }
}
