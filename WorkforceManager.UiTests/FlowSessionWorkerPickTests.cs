using System.Collections.Generic;
using System.Linq;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// قايمة عمال مرحلة عادية بعد فتحها لكل العمال (شوف
    /// FlowSessionViewModel.BuildStageWorkerPicks) — دالة نقية، نفس أسلوب
    /// WorkersViewModel.NextFlippedWorker (WorkerCardFlipTests) في اختبار
    /// منطق ViewModel من غير قاعدة بيانات.
    /// </summary>
    public class FlowSessionWorkerPickTests
    {
        private static Worker Worker(int id, string name, int sortOrder = 0, params (int StageId, int Stars)[] skills)
        {
            var worker = new Worker { Id = id, FullName = name, SortOrder = sortOrder };
            foreach (var (stageId, stars) in skills)
                worker.Skills.Add(new WorkerSkill { WorkerId = id, ProductionStageId = stageId, Stars = stars, Worker = worker });
            return worker;
        }

        [Fact]
        public void Everyone_appears_skilled_first_then_unskilled()
        {
            const int stage = 1;
            var skilledWorker = Worker(1, "ماهر", skills: (stage, 4));
            var unskilledWorker = Worker(2, "مبتدئ");
            var all = new List<Worker> { skilledWorker, unskilledWorker };
            var stageSkills = all.SelectMany(w => w.Skills).Where(s => s.ProductionStageId == stage);

            var picks = FlowSessionViewModel.BuildStageWorkerPicks(stageSkills, all, trainees: new());

            Assert.Equal(new[] { 1, 2 }, picks.Select(p => p.WorkerId));
        }

        [Fact]
        public void Skilled_group_sorted_by_stage_rating_descending()
        {
            const int stage = 1;
            var low = Worker(1, "ضعيف", skills: (stage, 2));
            var high = Worker(2, "ممتاز", skills: (stage, 5));
            var all = new List<Worker> { low, high };
            var stageSkills = all.SelectMany(w => w.Skills);

            var picks = FlowSessionViewModel.BuildStageWorkerPicks(stageSkills, all, trainees: new());

            Assert.Equal(new[] { 2, 1 }, picks.Select(p => p.WorkerId));
            Assert.Equal(5, picks[0].Stars);
        }

        [Fact]
        public void Unskilled_group_sorted_by_overall_rating_descending()
        {
            const int otherStage = 2; // الاتنين مالهمش مهارة على المرحلة المطلوبة، بس عندهم مهارة على مرحلة تانية
            var lowOverall = Worker(1, "متوسطه واطي", skills: (otherStage, 1));
            var highOverall = Worker(2, "متوسطه عالي", skills: (otherStage, 5));
            var all = new List<Worker> { lowOverall, highOverall };

            var picks = FlowSessionViewModel.BuildStageWorkerPicks(Enumerable.Empty<WorkerSkill>(), all, trainees: new());

            Assert.Equal(new[] { 2, 1 }, picks.Select(p => p.WorkerId));
            Assert.True(picks.All(p => p.HasTagLabel)); // "غير مؤهل بعد" على الاتنين
        }

        [Fact]
        public void Worker_with_no_skills_anywhere_sorts_last_not_excluded()
        {
            var hasOtherSkill = Worker(1, "عنده مهارة تانية", skills: (2, 3));
            var noSkillsAtAll = Worker(2, "مالوش مهارات خالص");
            var all = new List<Worker> { hasOtherSkill, noSkillsAtAll };

            var picks = FlowSessionViewModel.BuildStageWorkerPicks(Enumerable.Empty<WorkerSkill>(), all, trainees: new());

            Assert.Equal(2, picks.Count); // مش مستبعد
            Assert.Equal(2, picks.Last().WorkerId); // بس آخر واحد
            Assert.Equal(0, picks.Last().Stars);
        }
    }
}
