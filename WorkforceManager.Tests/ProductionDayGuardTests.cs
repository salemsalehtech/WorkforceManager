using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الحمايات اللي على يوم الإنتاج: حذف اليوم كامل، ومنع تكرار المرحلة
    /// بين النطاقات.
    ///
    /// دي أخطر حتة في التطبيق: حذف بيسيب نص يوم معناه أجور غلط لعمال
    /// حقيقيين آخر الأسبوع.
    /// </summary>
    public class ProductionDayGuardTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day1 => TestDatabase.Today;
        private static DateTime Day2 => TestDatabase.Today.AddDays(1);

        private const string Password = "1234";

        private async Task RecordAsync(int stageId, int pieces, DateTime date, int workerId)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .RecordProductionAsync(workerId, stageId, pieces, date, confirmOverride: true);
        }

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            // أول تسجيل لكلمة السر: مفيش قديمة، فالقديمة null
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        // ======================= حذف يوم كامل =======================

        // حذف يوم كامل بقى Tier B (بدون باسورد فوري، متغطى بتوقيع نهاية
        // اليوم) — اختبار "محتاج باسورد صح" اتشال، والسبب لسه إجباري

        [Fact]
        public async Task Deleting_a_whole_day_needs_a_written_reason()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, "   ");

            Assert.False(result.IsDeleted);
        }

        [Fact]
        public async Task Deleting_an_empty_day_says_so_instead_of_pretending_to_work()
        {
            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, "تنضيف");

            Assert.False(result.IsDeleted);
            Assert.Contains("مفيش أي إنتاج", result.Message);
        }

        [Fact]
        public async Task Deleting_a_whole_day_removes_every_record_and_logs_each_one()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 60, Day1, TestDatabase.WorkerSaidId);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, "اليوم اتسجل على تاريخ غلط");

            Assert.True(result.IsDeleted);

            var db = _db.GetService<AppDbContext>(scope);

            // مفيش سجل فاضل — لا ظاهر ولا متعلّم
            Assert.Empty(await db.DailyProductions.IgnoreQueryFilters().ToListAsync());

            // وكل واحد اتشال ليه حدث في السجل بسببه. بنفلتر على نوع
            // الحذف لأن التحضير نفسه (تسجيل الإنتاج وكلمة السر) بيكتب
            // أحداث كمان دلوقتي
            var events = await db.ActivityEvents
                .Where(e => e.EventType == ActivityEventType.ProductionRecordDeleted)
                .ToListAsync();

            Assert.Equal(2, events.Count);
            Assert.All(events, e => Assert.Equal("اليوم اتسجل على تاريخ غلط", e.Reason));
        }

        [Fact]
        public async Task Deleting_a_day_never_touches_another_day()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage1Id, 80, Day2, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, "غلط");

            var db = _db.GetService<AppDbContext>(scope);
            var survivor = Assert.Single(await db.DailyProductions.ToListAsync());
            Assert.Equal(Day2.Date, survivor.Date.Date);
        }

        [Fact]
        public async Task Deleting_a_whole_day_removes_the_attendance_of_every_worker_left_with_no_production()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage2Id, 60, Day1, TestDatabase.WorkerSaidId);

            // حضور "حاضر" مسجّل للاتنين — سواء اتسجل تلقائي أو بإيد
            // المدير من شاشة الحضور، النتيجة المتوقعة واحدة
            using (var scope = _db.CreateScope())
                await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(
                    Day1,
                    new[]
                    {
                        (TestDatabase.WorkerAhmedId, AttendanceStatus.Present),
                        (TestDatabase.WorkerSaidId, AttendanceStatus.Present)
                    });

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionDayAsync(Day1, "اليوم اتسجل على تاريخ غلط");

            using var check = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(check);

            // مفيش إنتاج فضل لأي عامل منهم في اليوم ده — الحضور اتشال معاه
            Assert.Empty(await checkDb.Attendances
                .Where(a => a.Date == Day1)
                .ToListAsync());
        }

        [Fact]
        public async Task Deleting_a_whole_day_keepsAWorkersAttendance_ifTheyStillHaveProductionOnAnotherDay()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            await RecordAsync(TestDatabase.BagStage1Id, 80, Day2, TestDatabase.WorkerAhmedId);

            using (var scope = _db.CreateScope())
                await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(
                    Day2, new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.Present) });

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .DeleteProductionDayAsync(Day1, "غلط");

            using var check = _db.CreateScope();
            var checkDb = _db.GetService<AppDbContext>(check);

            // حضور Day2 مالهوش علاقة بحذف Day1 — لسه له إنتاج فيه
            Assert.NotNull(await checkDb.Attendances.FirstOrDefaultAsync(
                a => a.WorkerId == TestDatabase.WorkerAhmedId && a.Date == Day2));
        }

        // ======================= منع تكرار المرحلة بين النطاقات =======================

        [Fact]
        public async Task A_stage_cannot_be_registered_twice_across_ranges()
        {
            // نطاقين متداخلين: من 1 لـ 2، وبعدين من 2 لـ 3. مرحلة 2 في
            // الاتنين — لو عدّت، العامل هياخد يوميتين على شغل يوم واحد
            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage2Id, PieceCount = 100 },
                new FlowRangeDto { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 100 }
            };

            var shares = new[] { TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id }
                .Select(id => new FlowShareDto
                {
                    ProductionStageId = id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = 100
                })
                .ToList();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, Day1, ranges, shares, confirmOverride: true));

            // الرسالة لازم تسمّي المرحلة والنطاقين — "فيه تداخل" لوحدها
            // بتخلي المستخدم يدوّر بنفسه على الغلط في 4 نطاقات
            Assert.Contains("خياطة", ex.Message);
            Assert.Contains("النطاق رقم 1", ex.Message);
            Assert.Contains("النطاق رقم 2", ex.Message);
        }

        [Fact]
        public async Task Ranges_that_touch_without_overlapping_are_fine()
        {
            // من 1 لـ 1، ومن 2 لـ 3 — ملزقين ومش متداخلين
            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage1Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 100 },
                new FlowRangeDto { FromStageId = TestDatabase.BagStage2Id, ToStageId = TestDatabase.BagStage3Id, PieceCount = 100 }
            };

            var shares = new[] { TestDatabase.BagStage1Id, TestDatabase.BagStage2Id, TestDatabase.BagStage3Id }
                .Select(id => new FlowShareDto
                {
                    ProductionStageId = id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = 100
                })
                .ToList();

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, Day1, ranges, shares, confirmOverride: true);

            Assert.Equal(3, (await _db.GetProductionAsync()).Count);
        }

        [Fact]
        public async Task A_reversed_range_names_which_range_is_wrong()
        {
            var ranges = new[]
            {
                new FlowRangeDto { FromStageId = TestDatabase.BagStage3Id, ToStageId = TestDatabase.BagStage1Id, PieceCount = 100 }
            };

            // العمال لازم يتبعتوا: "وزّع العمال الأول" بتتفحص قبل بنية
            // النطاقات، والاختبار ده بيخص رسالة النطاق المعكوس
            var shares = new[]
            {
                new FlowShareDto
                {
                    ProductionStageId = TestDatabase.BagStage1Id,
                    WorkerId = TestDatabase.WorkerAhmedId,
                    PieceCount = 100
                }
            };

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, Day1, ranges, shares, confirmOverride: true));

            Assert.Contains("النطاق رقم 1", ex.Message);
            Assert.Contains("معكوس", ex.Message);
        }
    }
}
