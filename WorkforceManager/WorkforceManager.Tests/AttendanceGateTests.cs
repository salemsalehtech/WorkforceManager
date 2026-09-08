using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// بوابة كلمة السر على سلفة/حافز (لسه Tier A)، وتأكيد إن حفظ الحضور
    /// بقى يشتغل بدون باسورد خالص (Tier B — شوف DailyOperationsSignOffServiceTests
    /// لاختبارات البوابة الجديدة).
    ///
    /// تسجيل إنتاج، تصحيح قطعة محفوظة، وحفظ الحضور **مبقاش عليهم بوابة
    /// باسورد فوري خالص** — كانوا هنا قبل كده واتشالوا عن قصد، مش نسيان.
    /// قفل/فتح إنتاج اليوم (DayClosureService) اتلغى بالكامل كميزة —
    /// اختباراته اتشالت من هنا مش لأنها بقت Tier B زي الباقي.
    /// </summary>
    public class AttendanceGateTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const string Password = "9999";

        private static DateTime Today => TestDatabase.Today;

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        private static (int, AttendanceStatus)[] OneAbsence =>
            new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission) };

        // ---------------- العمليات اللي لسه Tier A ----------------

        [Fact]
        public async Task Recording_a_wage_adjustment_with_a_wrong_password_is_refused()
        {
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
        public async Task The_right_password_lets_it_through()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();

            await _db.GetService<WageAdjustmentService>(scope).RecordAdjustmentAsync(
                TestDatabase.WorkerAhmedId, Today, WageAdjustmentType.Bonus, 200m,
                note: null, operationsPassword: Password);

            var db = _db.GetService<AppDbContext>(scope);
            Assert.Single(await db.WageAdjustments.ToListAsync());
        }

        // ---------------- الحضور بقى Tier B ----------------

        [Fact]
        public async Task Saving_attendance_works_with_no_password_at_all()
        {
            // مفيش بوابة خالص هنا دلوقتي — لا نجاح ولا رفض بيعتمد على
            // كلمة السر، حتى لو واحدة متسجّلة للحساب الحالي
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(Today, OneAbsence);

            Assert.Equal(1, result.SavedCount);
            Assert.Equal(1, result.AutoPenaltiesCreated);
        }

        [Fact]
        public async Task Attendance_still_saves_when_no_password_is_configured_at_all()
        {
            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(
                    Today,
                    new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.Present) });

            Assert.Equal(1, result.SavedCount);
        }

        [Fact]
        public async Task An_empty_batch_does_nothing()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var result = await _db.GetService<AttendanceService>(scope)
                .RecordAttendanceBatchAsync(Today, Array.Empty<(int, AttendanceStatus)>());

            Assert.Equal(0, result.SavedCount);
        }
    }
}
