using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// WeeklySummaryService.GetTeamWeeklySummaryAsync يحفظ ترتيب "أحسن 3
    /// عمال" الحقيقي (RecognitionRank 1/2/3) على كل فايز — منفصل عن ترتيب
    /// العرض الافتراضي (صافي اليوميات الخام، ordered) اللي القايمة بترجعه.
    /// الاختبار ده بيثبت إن الاتنين ممكن يختلفوا (عامل يومياته الخام أعلى
    /// بس تنوّعه/صعوبة مراحله أقل يترتب تاني في RecognitionRank رغم إنه
    /// الأول في ترتيب العرض) — وده بالظبط الباج اللي كان بيخلي الكارت
    /// الكبير في شاشة العمال يعرض أول واحد في القايمة مش الأول فعليًا.
    /// </summary>
    public class WeeklySummaryServiceRecognitionTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task RecordAsync(int stageId, int pieces, int workerId, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private async Task SetStageDifficultyAsync(int stageId, decimal multiplier)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var stage = await db.ProductionStages.FindAsync(stageId);
            stage!.DifficultyMultiplier = multiplier;
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task RecognitionRank_ReflectsTheAdjustedScore_NotTheRawNetWorkdaysOrder()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);

            // بطحتين محاكاة: RingStage2 معامل صعوبته ×2.0
            await SetStageDifficultyAsync(TestDatabase.RingStage2Id, 2.0m);

            // أحمد: مرحلة واحدة بس، 200 قطعة ÷ يومية 10 = 20 يومية خام
            // (صافي أعلى من سعيد، وترتيبه الأبجدي/رقم الـId قبله)
            await RecordAsync(TestDatabase.RingStage1Id, 200, TestDatabase.WorkerAhmedId, weekStart);

            // سعيد: مرحلتين، 80+80 قطعة = 16 يومية خام (أقل من أحمد)، بس
            // معامل صعوبة المرحلة التانية ×2.0 + معامل تنوّع ×0.95 يرفعوه
            // فوق أحمد في درجة التقييم
            await RecordAsync(TestDatabase.RingStage1Id, 80, TestDatabase.WorkerSaidId, weekStart);
            await RecordAsync(TestDatabase.RingStage2Id, 80, TestDatabase.WorkerSaidId, weekStart);

            var team = await _db.InScopeAsync<WeeklySummaryService, List<WorkerWeeklySummaryDto>>(
                service => service.GetTeamWeeklySummaryAsync(weekStart));

            var ahmed = team.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);
            var said = team.Single(w => w.WorkerId == TestDatabase.WorkerSaidId);

            // ترتيب العرض الافتراضي (صافي اليوميات الخام) — أحمد الأول فيه فعلاً
            Assert.Equal(20m, ahmed.NetWorkdays);
            Assert.Equal(16m, said.NetWorkdays);
            Assert.True(team.IndexOf(ahmed) < team.IndexOf(said));

            // لكن ترتيب "أحسن عامل" الحقيقي (RecognitionRank) — سعيد الأول
            // رغم يومياته الخام الأقل، لأن تنوّعه وصعوبة مرحلته الثانية
            // رفعوا درجته فوق أحمد
            Assert.Equal(1, said.RecognitionRank);
            Assert.Equal(2, ahmed.RecognitionRank);
            Assert.True(said.IsBestWorkerOfWeek);
            Assert.True(ahmed.IsBestWorkerOfWeek);
        }

        [Fact]
        public async Task RecognitionRank_IsNull_ForAnHourlyWorker_EvenWithHourlyWorkdaysCredited()
        {
            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);

            await _db.InScopeAsync<HourlyWorkdayService, HourlyWorkLog>(
                service => service.RecordHourlyWorkAsync(TestDatabase.WorkerMonaHourlyId, weekStart, 16));

            var team = await _db.InScopeAsync<WeeklySummaryService, List<WorkerWeeklySummaryDto>>(
                service => service.GetTeamWeeklySummaryAsync(weekStart));

            var mona = team.Single(w => w.WorkerId == TestDatabase.WorkerMonaHourlyId);

            Assert.True(mona.NetWorkdays > 0); // يومياته من الساعة موجودة فعلًا
            Assert.Null(mona.RecognitionRank);
            Assert.False(mona.IsBestWorkerOfWeek);
        }
    }
}
