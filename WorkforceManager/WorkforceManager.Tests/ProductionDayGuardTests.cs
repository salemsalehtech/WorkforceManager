using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الحمايات اللي على يوم الإنتاج: القفل، وحذف اليوم كامل، ومنع تكرار
    /// المرحلة بين النطاقات.
    ///
    /// دي أخطر حتة في التطبيق: قفل بينفتح بالغلط أو حذف بيسيب نص يوم
    /// معناه أجور غلط لعمال حقيقيين آخر الأسبوع.
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
            using var scope = _db.CreateScope();
            // أول تسجيل لكلمة السر: مفيش قديمة، فالقديمة null
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        // ======================= قفل اليوم =======================

        [Fact]
        public async Task Closing_a_day_stops_new_production_on_it()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(TestDatabase.BagStage2Id, 50, Day1, TestDatabase.WorkerSaidId));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task An_empty_day_can_still_be_closed_with_zeroes()
        {
            // يوم عطلة أو يوم وقفت فيه الخطوط: إقفاله بصفر تصريح إن اليوم
            // اتراجع فعلاً، مش إن حد نسي يسجّل
            using var scope = _db.CreateScope();
            var closure = await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            Assert.Equal(0, closure.CompletedPieces);
            Assert.Equal(0, closure.StartedPieces);
            Assert.True(await _db.GetService<DayClosureService>(scope).IsClosedAsync(Day1));
        }

        [Fact]
        public async Task Closing_an_already_closed_day_is_refused_with_a_clear_message()
        {
            using var scope = _db.CreateScope();
            var closure = _db.GetService<DayClosureService>(scope);

            await closure.CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => closure.CloseAsync(Day1));
            Assert.Contains("مقفول بالفعل", ex.Message);
        }

        [Fact]
        public async Task Reopening_a_day_that_is_not_closed_is_refused()
        {
            using var scope = _db.CreateScope();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<DayClosureService>(scope).ReopenAsync(Day1));

            Assert.Contains("مش مقفول", ex.Message);
        }

        [Fact]
        public async Task A_closed_day_leaves_no_partial_state_when_reopened()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var closure = _db.GetService<DayClosureService>(scope);

            await closure.CloseAsync(Day1);
            await closure.ReopenAsync(Day1);

            Assert.False(await closure.IsClosedAsync(Day1));

            // والتسجيل رجع يشتغل — القفل مساش السجلات نفسها
            await RecordAsync(TestDatabase.BagStage2Id, 50, Day1, TestDatabase.WorkerSaidId);
            Assert.Equal(2, (await _db.GetProductionAsync()).Count);
        }

        [Fact]
        public async Task Closing_one_day_never_touches_another()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var closure = _db.GetService<DayClosureService>(scope);
            await closure.CloseAsync(Day1);

            Assert.True(await closure.IsClosedAsync(Day1));
            Assert.False(await closure.IsClosedAsync(Day2));

            // وبكرة شغال عادي
            await RecordAsync(TestDatabase.BagStage1Id, 80, Day2, TestDatabase.WorkerAhmedId);
        }

        // ======================= حذف يوم كامل =======================

        [Fact]
        public async Task Deleting_a_whole_day_needs_the_operations_password()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, "كلمة غلط", "تجربة");

            Assert.False(result.IsDeleted);

            // ولا سجل اتشال
            var db = _db.GetService<AppDbContext>(scope);
            Assert.Empty(await db.DailyProductions.IgnoreQueryFilters()
                .Where(p => p.IsDeleted).ToListAsync());
        }

        [Fact]
        public async Task Deleting_a_whole_day_needs_a_written_reason()
        {
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, Password, "   ");

            Assert.False(result.IsDeleted);
        }

        [Fact]
        public async Task Deleting_an_empty_day_says_so_instead_of_pretending_to_work()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, Password, "تنضيف");

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
                .DeleteProductionDayAsync(Day1, Password, "اليوم اتسجل على تاريخ غلط");

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
                .DeleteProductionDayAsync(Day1, Password, "غلط");

            var db = _db.GetService<AppDbContext>(scope);
            var survivor = Assert.Single(await db.DailyProductions.ToListAsync());
            Assert.Equal(Day2.Date, survivor.Date.Date);
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

        [Fact]
        public async Task A_closed_day_also_blocks_the_single_record_path()
        {
            // القفل كان متحطّ في مسار رحلة الإنتاج بس، فالمسار ده كان
            // بيلفّ حواليه — يوم مقفول ينفع يتسجل عليه سجل واحد عادي
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using (var scope = _db.CreateScope())
                await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RecordAsync(TestDatabase.BagStage2Id, 50, Day1, TestDatabase.WorkerSaidId));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task A_closed_day_also_blocks_editing_a_saved_record()
        {
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);
            var record = Assert.Single(await _db.GetProductionAsync());

            using var scope = _db.CreateScope();
            await _db.GetService<DayClosureService>(scope).CloseAsync(Day1);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WorkdayCalculationService>(scope).UpdateProductionAsync(record.Id, 60));

            Assert.Contains("مقفول", ex.Message);
        }

        [Fact]
        public async Task A_closed_day_can_still_be_corrected_by_deleting_with_a_reason()
        {
            // القفل بيمنع الكتابة الصامتة، مش التصحيح المسؤول: الحذف
            // بكلمة سر وسبب مكتوب هو الطريق المقصود لإصلاح يوم اتقفل غلط
            await SetPasswordAsync();
            await RecordAsync(TestDatabase.BagStage1Id, 100, Day1, TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            await _db.GetService<DayClosureService>(scope).CloseAsync(Day1, Password);

            var result = await _db.GetService<WorkdayCalculationService>(scope)
                .DeleteProductionDayAsync(Day1, Password, "اليوم اتقفل بالغلط");

            Assert.True(result.IsDeleted);
        }
    }
}
