using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// بوابة كلمة السر على حفظ الحضور.
    ///
    /// الحضور دخل قايمة العمليات الحساسة رغم إنه شغل يومي، والفرق إنه
    /// بيتحفظ **دفعة واحدة لكل القسم**: كلمة سر واحدة في اليوم مش واحدة
    /// لكل عامل. وهو بيولّد جزاءات غياب بتنقص من الأجر، فهو عمليًا
    /// عملية بتلمس فلوس.
    /// </summary>
    public class AttendanceGateTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const string Password = "9999";

        private static DateTime Today => TestDatabase.Today;

        private async Task SetPasswordAsync()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        private static (int, AttendanceStatus)[] OneAbsence =>
            new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission) };

        // ---------------- العمليات اللي دخلت البوابة بعد كده ----------------

        [Fact]
        public async Task Recording_production_with_a_wrong_password_is_refused()
        {
            // الإنتاج هو اللي اليوميات بتتحسب منه، واليوميات هي الأجر
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductBagId, Today,
                    new[]
                    {
                        new FlowRangeDto
                        {
                            FromStageId = TestDatabase.BagStage1Id,
                            ToStageId = TestDatabase.BagStage1Id, PieceCount = 100
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.BagStage1Id,
                            WorkerId = TestDatabase.WorkerAhmedId, PieceCount = 100
                        }
                    },
                    confirmOverride: true, operationsPassword: "غلط"));

            Assert.NotEmpty(ex.Message);

            // ولا سجل اتكتب — الرفض قبل أي كتابة
            Assert.Empty(await _db.GetProductionAsync());
        }

        [Fact]
        public async Task Recording_a_wage_adjustment_with_a_wrong_password_is_refused()
        {
            // دي كانت الحركة الوحيدة اللي بتلمس فلوس من غير كلمة سر
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<WageAdjustmentService>(scope).RecordAdjustmentAsync(
                    TestDatabase.WorkerAhmedId, Today, WageAdjustmentType.Bonus, 200m,
                    note: null, operationsPassword: "غلط"));

            Assert.NotEmpty(ex.Message);

            var db = _db.GetService<AppDbContext>(scope);
            Assert.Empty(await db.WageAdjustments.ToListAsync());
        }

        [Fact]
        public async Task Correcting_a_saved_piece_count_with_a_wrong_password_is_refused()
        {
            // تعديل رقم إنتاج محفوظ كان بيعدّي من غير كلمة سر بينما
            // **حذف** نفس السجل بيتطلبها — والاتنين بيغيّروا نفس الأجر
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100, Today);

            var recordId = (await _db.GetProductionAsync()).Single().Id;
            await SetPasswordAsync();

            using (var scope = _db.CreateScope())
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    _db.GetService<WorkdayCalculationService>(scope)
                        .UpdateProductionAsync(recordId, 999, "غلط"));

                Assert.NotEmpty(ex.Message);
            }

            // الرقم زي ما هو — الرفض قبل أي كتابة
            Assert.Equal(100, (await _db.GetProductionAsync()).Single().PieceCount);
        }

        [Fact]
        public async Task Closing_the_day_with_a_wrong_password_is_refused()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<DayClosureService>(scope).CloseAsync(Today, "غلط"));

            Assert.NotEmpty(ex.Message);
            Assert.False(await _db.GetService<DayClosureService>(scope).IsClosedAsync(Today));
        }

        [Fact]
        public async Task The_right_password_lets_all_three_through()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();

            await _db.GetService<WageAdjustmentService>(scope).RecordAdjustmentAsync(
                TestDatabase.WorkerAhmedId, Today, WageAdjustmentType.Bonus, 200m,
                note: null, operationsPassword: Password);

            await _db.GetService<DayClosureService>(scope).CloseAsync(Today, Password);

            var db = _db.GetService<AppDbContext>(scope);
            Assert.Single(await db.WageAdjustments.ToListAsync());
            Assert.True(await _db.GetService<DayClosureService>(scope).IsClosedAsync(Today));
        }

        [Fact]
        public async Task Saving_attendance_with_a_wrong_password_is_refused()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<AttendanceService>(scope)
                    .RecordAttendanceBatchAsync(Today, OneAbsence, "كلمة غلط"));

            Assert.NotEmpty(ex.Message);

            // ولا حالة اتحفظت — الرفض قبل أي كتابة
            var db = _db.GetService<AppDbContext>(scope);
            Assert.Empty(await db.Attendances.ToListAsync());
        }

        [Fact]
        public async Task A_refused_save_creates_no_auto_penalty_either()
        {
            // الغياب بدون إذن بيولّد جزاء نص يومية. لو الرفض سرّب جزاء
            // من غير حضور، العامل هياخد خصم على غياب محصلش تسجيله أصلاً
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<AttendanceService>(scope)
                    .RecordAttendanceBatchAsync(Today, OneAbsence, "غلط"));

            var db = _db.GetService<AppDbContext>(scope);
            Assert.Empty(await db.Penalties.ToListAsync());
        }

        [Fact]
        public async Task Saving_attendance_with_the_right_password_works()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(Today, OneAbsence, Password);

            Assert.Equal(1, result.SavedCount);
            Assert.Equal(1, result.AutoPenaltiesCreated);
        }

        [Fact]
        public async Task Attendance_still_saves_when_no_password_is_configured_yet()
        {
            // تركيب جديد لسه محدش ظبط فيه كلمة سر: البوابة بتعدّي بدل ما
            // تقفل المستخدم بره تطبيقه
            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(
                    Today,
                    new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.Present) });

            Assert.Equal(1, result.SavedCount);
        }

        [Fact]
        public async Task An_empty_batch_never_reaches_the_gate()
        {
            // مفيش حاجة تتحفظ = مفيش سبب يسأل كلمة سر
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(Today, Array.Empty<(int, AttendanceStatus)>());

            Assert.Equal(0, result.SavedCount);
        }
    }
}
