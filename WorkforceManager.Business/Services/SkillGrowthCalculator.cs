namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// نمو تقييم مهارة اتضافت تلقائيًا (شوف WorkerManagementService.
    /// AutoAssignSkillAsync) — دالة نقية، مفيش قاعدة بيانات هنا خالص،
    /// عشان القاعدة نفسها تتفهم وتتختبر من غير أي إعداد.
    ///
    /// هجين عن قصد (اتفق عليه): مش عدد مرات الاختيار بس (عامل بيتكرر
    /// اختياره بإنتاج ضعيف مايستاهلش يزيد)، ومش الجودة بس (يوم واحد
    /// ممتاز مايكفيش يثبت حاجة). لازم الاتنين مع بعض لكل درجة.
    /// </summary>
    public static class SkillGrowthCalculator
    {
        /// <summary>سقف النمو التلقائي — 5 نجوم قرار مدير بس، أبدًا مش بيحصل لوحده</summary>
        public const int AutoGrowthCap = 4;

        /// <summary>(النجمة المستهدفة، أقل عدد اختيارات لازم يكون وصله قبلها)</summary>
        private static readonly (int Stars, int MinTimesSelected)[] Milestones =
        {
            (2, 3),
            (3, 8),
            (4, 15)
        };

        /// <summary>
        /// النجمة التالية، أو نفس الحالية لو الشروط مش مستوفاة. أبدًا مش
        /// بترجع رقم أقل من currentStars — النمو التلقائي في اتجاه واحد
        /// بس، أي نزول قرار يدوي دايمًا.
        ///
        /// measuredRatio null لسه لما مفيش عينة كفاية (أقل من
        /// SkillRatingService.MinSampleDays أيام إنتاج) — من غير عينة
        /// مفيش نمو، مهما كان عدد الاختيارات.
        /// </summary>
        public static int ComputeNextStars(int currentStars, int timesSelected, decimal? measuredRatio)
        {
            if (measuredRatio is null) return currentStars;

            var qualityStars = SkillRatingService.StarsForRatio(measuredRatio.Value);

            var candidate = currentStars;
            foreach (var (stars, minSelected) in Milestones)
                if (timesSelected >= minSelected && qualityStars >= stars)
                    candidate = stars;

            return Math.Max(currentStars, Math.Min(candidate, AutoGrowthCap));
        }
    }
}
