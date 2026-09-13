using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قواعد ترتيب "أحسن عامل" الأسبوعي/الشهري — قاعدة عمل نقية زي
    /// AbsenceDeductionRule/WorkerFilterRules، مفيش استعلام قاعدة بيانات
    /// جواها.
    ///
    /// المشكلة اللي الترتيب ده بيحلها: مرحلة يوميتها منخفضة بطبيعتها
    /// (دقيقة/صعبة) بتخلي العامل عليها يظهر أقل إنتاجًا من عامل على مرحلة
    /// سهلة عالية الإنتاج، مع إن مهارته فعليًا أعلى. الحل: معامل صعوبة
    /// يدوي لكل مرحلة (ProductionStage.DifficultyMultiplier) + معامل تنوّع
    /// حسب عدد المراحل المختلفة اللي العامل اشتغل عليها — عشان عامل واحد
    /// شغال على مرحلة واحدة بكمية كبيرة ميبقاش "أحسن عامل" لوحده.
    ///
    /// **درجة الترتيب دي مؤقتة وبتُحسب وتُنسى فورًا** — مش NetWorkdays ولا
    /// NetWageEgp، ومالهاش أي أثر على أجر أو تقرير إنتاج.
    /// </summary>
    public static class WorkerRecognitionRules
    {
        // معاملات التنوّع — كل أرقام الصيغة هنا، مكان واحد لو احتجنا نظبطها
        private const decimal SingleStageFactor = 0.85m;
        private const decimal TwoStagesFactor = 0.95m;
        private const decimal ThreeStagesFactor = 1.00m;
        private const decimal FourPlusStagesFactor = 1.05m;

        /// <summary>تفاصيل حساب درجة عامل واحد — أساس نافذة "ليه فاز؟" ومصدر واحد لـ RecognitionScore</summary>
        public record RecognitionBreakdown(
            decimal AdjustedWorkdays,
            int DistinctStageCount,
            decimal DiversityFactor,
            decimal AbsenceDeduction,
            decimal PenaltyDeduction,
            decimal FinalScore);

        /// <summary>
        /// درجة عامل واحد للترتيب بتفاصيلها = (مجموع نصيب كل مرحلة اشتغل عليها
        /// × معامل صعوبتها الحالي) × معامل التنوّع − خصم الغياب − خصم الجزاءات.
        /// </summary>
        public static RecognitionBreakdown Explain(
            WorkerWeeklySummaryDto summary, IReadOnlyDictionary<int, decimal> difficultyByStageId)
        {
            var adjustedWorkdays = summary.Breakdown.Sum(b =>
                b.Workdays * difficultyByStageId.GetValueOrDefault(b.ProductionStageId, 1.0m));

            var diversityFactor = summary.Breakdown.Count switch
            {
                <= 1 => SingleStageFactor,
                2 => TwoStagesFactor,
                3 => ThreeStagesFactor,
                _ => FourPlusStagesFactor
            };

            var finalScore = adjustedWorkdays * diversityFactor - summary.AbsenceDeduction - summary.PenaltyDeduction;

            return new RecognitionBreakdown(
                adjustedWorkdays, summary.Breakdown.Count, diversityFactor,
                summary.AbsenceDeduction, summary.PenaltyDeduction, finalScore);
        }

        /// <summary>درجة عامل واحد للترتيب — غلاف رفيع فوق Explain لمين محتاج الرقم النهائي بس</summary>
        public static decimal RecognitionScore(
            WorkerWeeklySummaryDto summary, IReadOnlyDictionary<int, decimal> difficultyByStageId) =>
            Explain(summary, difficultyByStageId).FinalScore;

        /// <summary>
        /// ترتيب فريق كامل لتحديد "أحسن عمال" الفترة. شرط الأهلية: عامل
        /// بالساعة برة المقارنة خالص (مش بينتج قطع أصلًا فمعامل الصعوبة
        /// والتنوّع مالهومش معنى بالنسبة له)، وبعدين أنتج فعلًا وصافيه
        /// موجب. الترتيب بالدرجة، وعند التعادل الأكتر تنوّعًا فالاسم
        /// أبجديًا (زي نمط SkillRatingService.Rank — ترتيب ثابت مش عشوائي
        /// عند التعادل).
        /// </summary>
        public static List<WorkerWeeklySummaryDto> Rank(
            IReadOnlyList<WorkerWeeklySummaryDto> summaries,
            IReadOnlyDictionary<int, decimal> difficultyByStageId) =>
            summaries
                .Where(s => !s.IsHourly && s.ProducedWorkdays > 0 && s.NetWorkdays > 0)
                .OrderByDescending(s => RecognitionScore(s, difficultyByStageId))
                .ThenByDescending(s => s.Breakdown.Count)
                .ThenBy(s => s.WorkerName)
                .ToList();
    }
}
