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

        /// <summary>
        /// درجة عامل واحد للترتيب = (مجموع نصيب كل مرحلة اشتغل عليها ×
        /// معامل صعوبتها الحالي) × معامل التنوّع − خصم الغياب − خصم الجزاءات.
        /// </summary>
        public static decimal RecognitionScore(
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

            return adjustedWorkdays * diversityFactor - summary.AbsenceDeduction - summary.PenaltyDeduction;
        }

        /// <summary>
        /// ترتيب فريق كامل لتحديد "أحسن عمال" الفترة — نفس شرط الأهلية
        /// القديم (أنتج فعلًا وصافيه موجب)، بعدين بالدرجة، وعند التعادل
        /// الأكتر تنوّعًا فالاسم أبجديًا (زي نمط SkillRatingService.Rank —
        /// ترتيب ثابت مش عشوائي عند التعادل).
        /// </summary>
        public static List<WorkerWeeklySummaryDto> Rank(
            IReadOnlyList<WorkerWeeklySummaryDto> summaries,
            IReadOnlyDictionary<int, decimal> difficultyByStageId) =>
            summaries
                .Where(s => s.ProducedWorkdays > 0 && s.NetWorkdays > 0)
                .OrderByDescending(s => RecognitionScore(s, difficultyByStageId))
                .ThenByDescending(s => s.Breakdown.Count)
                .ThenBy(s => s.WorkerName)
                .ToList();
    }
}
