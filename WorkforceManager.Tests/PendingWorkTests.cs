using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الشغل الواقف في نص الخط.
    ///
    /// الفكرة: القطعة اللي خلصت المرحلة 1 وماخلصتش المرحلة 2 هي بالتعريف
    /// واقفة قدام المرحلة 2. مفيش جدول بيتتبّع القطع — الرقم محسوب من
    /// سجلات الإنتاج اللي المستخدم بيدخّلها أصلاً.
    ///
    /// خط الشنطة: 1.قص → 2.خياطة → 3.تشطيب
    /// </summary>
    public class PendingWorkTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => TestDatabase.Today.AddDays(1);

        private async Task RecordAsync(int stageId, int pieces, DateTime date, int workerId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private async Task<Business.DTOs.ProductPendingDto> BagPendingAsync(DateTime asOf)
        {
            using var scope = _db.CreateScope();
            var result = await _db.GetService<PendingWorkService>(scope)
                .GetForProductAsync(TestDatabase.ProductBagId, asOf);

            Assert.NotNull(result);
            return result!;
        }

        // ======================= الحساب الأساسي =======================

        [Fact]
        public async Task Work_that_finished_the_whole_line_leaves_nothing_pending()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 100, Day1, TestDatabase.WorkerSaidId);
            await RecordAsync(TestDatabase.BagStage3Id, 100, Day1, TestDatabase.WorkerMonaHourlyId);

            var pending = await BagPendingAsync(Day1);

            Assert.False(pending.HasPending);
            Assert.Equal(0, pending.TotalPending);
        }

        [Fact]
        public async Task Work_that_stopped_mid_line_is_pending_at_the_next_stage()
        {
            // 100 قصّوا، ومحدش خاطهم → 100 واقفين قدام الخياطة
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            var pending = await BagPendingAsync(Day1);

            Assert.True(pending.HasPending);
            Assert.Equal(100, pending.TotalPending);

            var stage = Assert.Single(pending.Resumable);
            Assert.Equal("خياطة", stage.StageName);
            Assert.Equal(100, stage.PendingPieces);
            Assert.Equal("قص", stage.PreviousStageName);
        }

        [Fact]
        public async Task Partial_progress_leaves_the_rest_pending()
        {
            // 100 اتقصّوا، 60 بس اتخاطوا → 40 واقفين قدام الخياطة،
            // و60 واقفين قدام التشطيب
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 60, Day1, TestDatabase.WorkerSaidId);

            var pending = await BagPendingAsync(Day1);

            Assert.Equal(40, pending.Stages.Single(s => s.StageName == "خياطة").PendingPieces);
            Assert.Equal(60, pending.Stages.Single(s => s.StageName == "تشطيب").PendingPieces);

            // الإجمالي = اللي دخل الخط ناقص اللي خرج منه
            Assert.Equal(100, pending.TotalPending);
        }

        [Fact]
        public async Task Pending_work_carries_to_the_next_day_without_any_carry_over_step()
        {
            // مفيش خطوة "ترحيل" — الرقم محسوب من كل التاريخ، فبيبان بكرة لوحده
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            var tomorrow = await BagPendingAsync(Day2);

            Assert.Equal(100, tomorrow.TotalPending);
        }

        [Fact]
        public async Task Finishing_the_pending_work_clears_it()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            // بكرة: كمّلنا المرحلتين الباقيين
            await RecordAsync(TestDatabase.BagStage2Id, 100, Day2, TestDatabase.WorkerSaidId);
            await RecordAsync(TestDatabase.BagStage3Id, 100, Day2, TestDatabase.WorkerMonaHourlyId);

            var after = await BagPendingAsync(Day2);

            Assert.False(after.HasPending);
            Assert.Equal(0, after.TotalPending);
        }

        [Fact]
        public async Task Pending_is_computed_up_to_the_asked_date_not_beyond_it()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 100, Day2, TestDatabase.WorkerSaidId);

            // يوم 1: الخياطة لسه ماتعملتش، فالـ 100 واقفين
            Assert.Equal(100, (await BagPendingAsync(Day1)).TotalPending);

            // يوم 2: اتخاطوا، فالواقف اتنقل قدام التشطيب — بس لسه 100
            var day2 = await BagPendingAsync(Day2);
            Assert.Equal(100, day2.TotalPending);
            Assert.Equal("تشطيب", Assert.Single(day2.Resumable).StageName);
        }

        // ======================= غلط الإدخال =======================

        [Fact]
        public async Task A_stage_recorded_above_the_one_before_it_is_flagged_not_hidden()
        {
            // 50 اتقصّوا و80 اتخاطوا — مستحيل، القطعة لازم تتقص الأول.
            // إخفاء الغلط بتصفير صامت بيخلي المستخدم يبني على رقم غلط.
            await RecordAsync(TestDatabase.BagStage1Id, 50, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 80, Day1, TestDatabase.WorkerSaidId);

            var pending = await BagPendingAsync(Day1);

            Assert.True(pending.HasDataError);

            var flagged = pending.Stages.Single(s => s.IsDataError);
            Assert.Equal("خياطة", flagged.StageName);
            Assert.Equal(30, flagged.ExcessPieces);

            // الرسالة لازم تسمّي المرحلتين والرقم عشان المستخدم يعرف يصلّح
            Assert.Contains("خياطة", flagged.ErrorText);
            Assert.Contains("قص", flagged.ErrorText);
            Assert.Contains("30", flagged.ErrorText);
        }

        [Fact]
        public async Task A_flagged_stage_is_never_offered_for_resuming()
        {
            // مرحلة أرقامها متناقضة مش هينفع نكمّل منها — الرقم مش موثوق
            await RecordAsync(TestDatabase.BagStage1Id, 50, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 80, Day1, TestDatabase.WorkerSaidId);

            var pending = await BagPendingAsync(Day1);

            Assert.DoesNotContain(pending.Resumable, s => s.StageName == "خياطة");
        }

        [Fact]
        public async Task A_negative_total_never_leaks_out_as_a_number()
        {
            // آخر مرحلة عليها أكتر من أول مرحلة = بيانات متناقضة.
            // "واقف بالسالب" مالوش أي معنى للمستخدم.
            await RecordAsync(TestDatabase.BagStage1Id, 50, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage3Id, 200, Day1, TestDatabase.WorkerSaidId);

            var pending = await BagPendingAsync(Day1);

            Assert.Equal(0, pending.TotalPending);
            Assert.True(pending.HasDataError);
        }

        // ======================= الشمول =======================

        [Fact]
        public async Task Products_with_nothing_pending_are_left_out_of_the_list()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var all = await _db.GetService<PendingWorkService>(scope).GetAllAsync(Day1);

            // الشنطة بس — باقي المنتجات مفيش عليها شغل
            var bag = Assert.Single(all);
            Assert.Equal(TestDatabase.ProductBagId, bag.ProductId);
        }

        [Fact]
        public async Task The_last_stage_is_exposed_so_the_button_knows_where_to_stop()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            var pending = await BagPendingAsync(Day1);

            Assert.Equal(TestDatabase.BagStage3Id, pending.LastStageId);
        }
    }
}
