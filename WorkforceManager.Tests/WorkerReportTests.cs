using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تقرير العامل: الأرقام اللي بيتبني عليها قرار — أجره، ومهاراته.
    ///
    /// التقرير ده هو اللي بيتطبع ويتسلّم، فأي رقم غلط فيه بيتصرف فلوس
    /// غلط. الاختبارات هنا بتمسك التلات حالات اللي بيغلط فيها الناس:
    /// جمع القطع، وصافي بالسالب، وتقييم متخصص في مراحل قليلة.
    /// </summary>
    public class WorkerReportTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(int stageId, int pieces, int workerId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, Day, confirmOverride: true);
        }

        private async Task<Business.DTOs.WorkerProductionReportDto> ReportAsync(
            int workerId = TestDatabase.WorkerAhmedId)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<ProductionReportService>(scope)
                .GetWorkerReportAsync(workerId, Day, Day);
        }

        /// <summary>يظبط نجوم مهارة موجودة أصلاً (المزروعة كلها 3)</summary>
        private async Task SetStarsAsync(int workerId, int stageId, int stars)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var skill = await db.WorkerSkills
                .FirstAsync(s => s.WorkerId == workerId && s.ProductionStageId == stageId);
            skill.Stars = stars;
            await db.SaveChangesAsync();
        }

        // ======================= الإنتاج =======================

        [Fact]
        public async Task A_workers_pieces_count_once_per_stage_he_worked_on()
        {
            // 100 قطعة عدّت على مرحلتين والعامل شغّال على الاتنين = شغل
            // مرتين. الرقم ده مقياس **شغله هو**، مش إنتاج المنتج — ولو
            // اتحوّل لـ "آخر مرحلة بس" العامل اللي على القص هياخد صفر.
            await RecordAsync(TestDatabase.BagStage1Id, 100, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 100, TestDatabase.WorkerAhmedId);

            var report = await ReportAsync();

            Assert.Equal(200, report.TotalPieces);
            Assert.Equal(2, report.ByProductStage.Count);
        }

        // ======================= الأجر =======================

        [Fact]
        public async Task Advances_bigger_than_the_wage_are_flagged_not_zeroed()
        {
            // العامل أنتج كوتة واحدة (200 ج) وخد سلفة 500 ج → مدين بـ 300.
            // تصفير الرقم بيخفي إن السلف اتصرفت فعلًا.
            await RecordAsync(TestDatabase.BagStage1Id, 10, TestDatabase.WorkerAhmedId);

            using (var scope = _db.CreateScope())
            {
                await _db.GetService<WageAdjustmentService>(scope).RecordAdjustmentAsync(
                    TestDatabase.WorkerAhmedId, Day, WageAdjustmentType.Advance, 500m);
            }

            var report = await ReportAsync();

            Assert.True(report.IsWageNegative);
            Assert.True(report.NetWageEgp < 0);
        }

        [Fact]
        public async Task A_worker_with_no_daily_rate_is_flagged_so_the_zero_is_explained()
        {
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var worker = await db.Workers.FirstAsync(w => w.Id == TestDatabase.WorkerSaidId);
                worker.DailyWageEgp = 0m;
                await db.SaveChangesAsync();
            }

            await RecordAsync(TestDatabase.BagStage1Id, 100, TestDatabase.WorkerSaidId);

            var report = await ReportAsync(TestDatabase.WorkerSaidId);

            Assert.True(report.HasNoWageRate);
            Assert.Equal(0m, report.NetWageEgp);
        }

        [Fact]
        public async Task A_normal_wage_raises_neither_warning()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, TestDatabase.WorkerAhmedId);

            var report = await ReportAsync();

            Assert.False(report.IsWageNegative);
            Assert.False(report.HasNoWageRate);
        }

        // ======================= المهارات =======================

        [Fact]
        public async Task The_report_carries_his_rating_per_product_highest_first()
        {
            // الشنطة 5 نجوم، الدبلة 2 — لازم الشنطة تيجي الأول
            await SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 5);
            await SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 5);
            await SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 5);
            await SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage1Id, 2);
            await SetStarsAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage2Id, 2);

            var report = await ReportAsync();

            var bag = report.Skills.First();
            Assert.Equal("شنطة", bag.ProductName);
            Assert.Equal(5m, bag.AverageStars);
            Assert.Equal(3, bag.KnownStages);

            var ring = report.Skills.Single(s => s.ProductName == "دبلة");
            Assert.Equal(2m, ring.AverageStars);
        }

        [Fact]
        public async Task Stages_he_is_not_linked_to_do_not_drag_his_average_down()
        {
            // متخصص في مرحلة واحدة من تلاتة بـ 5 نجوم = 5، مش 1.67.
            // نفس قاعدة SkillRatingService.ProductStars — التقرير بينادي
            // عليها مش بيعيد كتابتها.
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var extra = await db.WorkerSkills
                    .Where(s => s.WorkerId == TestDatabase.WorkerSaidId &&
                                (s.ProductionStageId == TestDatabase.BagStage2Id ||
                                 s.ProductionStageId == TestDatabase.BagStage3Id))
                    .ToListAsync();
                db.WorkerSkills.RemoveRange(extra);
                await db.SaveChangesAsync();
            }
            await SetStarsAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage1Id, 5);

            var report = await ReportAsync(TestDatabase.WorkerSaidId);

            var bag = report.Skills.Single(s => s.ProductName == "شنطة");
            Assert.Equal(5m, bag.AverageStars);
            Assert.Equal(1, bag.KnownStages);
        }

        [Fact]
        public async Task A_worker_with_no_skills_gets_an_empty_list_not_a_crash()
        {
            // منى بالساعة — مالهاش أي مهارة
            var report = await ReportAsync(TestDatabase.WorkerMonaHourlyId);

            Assert.Empty(report.Skills);
        }
    }
}
