using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// قاعدة ترتيب "أحسن عامل" (WorkerRecognitionRules) — دالة نقية،
    /// مفيش قاعدة بيانات هنا خالص. الهدف: عامل بمهارة متنوّعة وإنتاج
    /// أقل شوية يترتب فوق عامل شغال على مرحلة واحدة بكمية كبيرة، ومعامل
    /// صعوبة المرحلة بيرفع الترتيب من غير ما يلمس NetWorkdays/NetWageEgp.
    /// </summary>
    public class WorkerRecognitionRulesTests
    {
        private static StageBreakdownDto Stage(int stageId, decimal workdays) => new()
        {
            ProductionStageId = stageId,
            PieceCount = (int)(workdays * 100),
            PiecesPerWorkday = 100
        };

        private static WorkerWeeklySummaryDto Summary(int workerId, string name, params StageBreakdownDto[] stages) => new()
        {
            WorkerId = workerId,
            WorkerName = name,
            ProducedWorkdays = stages.Sum(s => s.Workdays),
            Breakdown = stages.ToList()
        };

        private static readonly Dictionary<int, decimal> NormalDifficulty = new();

        [Fact]
        public void ADiverseWorker_OutranksASingleStageWorker_EvenWithSlightlyLessOutput()
        {
            // شغال 10 يوميات على مرحلة واحدة بس = ×0.85
            var singleStage = Summary(1, "أحمد", Stage(1, 10m));

            // شغال 9 يوميات موزّعة على 3 مراحل = ×1.00
            var diverse = Summary(2, "محمد", Stage(2, 3m), Stage(3, 3m), Stage(4, 3m));

            var ranked = WorkerRecognitionRules.Rank(new[] { singleStage, diverse }, NormalDifficulty);

            Assert.Equal(2, ranked[0].WorkerId); // محمد الأول رغم إنه أنتج أقل
        }

        [Fact]
        public void AHigherDifficultyMultiplier_RaisesTheScore_WithoutTouchingNetWorkdaysOrWage()
        {
            var summary = Summary(1, "أحمد", Stage(10, 5m));
            summary.DailyWageEgp = 200m;

            var netBefore = summary.NetWorkdays;
            var wageBefore = summary.NetWageEgp;

            var normalScore = WorkerRecognitionRules.RecognitionScore(summary, NormalDifficulty);
            var hardScore = WorkerRecognitionRules.RecognitionScore(
                summary, new Dictionary<int, decimal> { [10] = 3.0m });

            Assert.True(hardScore > normalScore);
            Assert.Equal(netBefore, summary.NetWorkdays);
            Assert.Equal(wageBefore, summary.NetWageEgp);
        }

        [Fact]
        public void OnATie_TheMoreDiverseWorkerWins_ThenAlphabeticalName()
        {
            // نفس الدرجة بالظبط: 19 × 0.85 = 17 × 0.95 = 16.15
            var singleStage = Summary(1, "ياسر", Stage(1, 19m));
            var twoStages = Summary(2, "بيتر", Stage(2, 10m), Stage(3, 7m));

            Assert.Equal(
                WorkerRecognitionRules.RecognitionScore(singleStage, NormalDifficulty),
                WorkerRecognitionRules.RecognitionScore(twoStages, NormalDifficulty));

            var ranked = WorkerRecognitionRules.Rank(new[] { singleStage, twoStages }, NormalDifficulty);

            Assert.Equal(2, ranked[0].WorkerId); // الأكتر تنوّعًا يقدّم عند التعادل الفعلي
        }

        [Fact]
        public void WorkersWithNoProduction_OrNonPositiveNet_AreNeverRanked()
        {
            var noProduction = new WorkerWeeklySummaryDto { WorkerId = 1, WorkerName = "بلا إنتاج", ProducedWorkdays = 0 };
            var negativeNet = Summary(2, "خصومات كتير", Stage(1, 1m));
            negativeNet.PenaltyDeduction = 5m; // NetWorkdays يبقى سالب

            var ranked = WorkerRecognitionRules.Rank(new[] { noProduction, negativeNet }, NormalDifficulty);

            Assert.Empty(ranked);
        }
    }
}
