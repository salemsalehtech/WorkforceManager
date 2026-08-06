using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الجزاءات: بوابة كلمة السر، والتعديل، وحماية الجزاء التلقائي.
    ///
    /// الجزاء بيخصم من أجر عامل حقيقي، فهو عملية بتلمس فلوس. وأخطر حاجة
    /// هنا هي **التفرقة بين اليدوي والتلقائي**: الجزاء التلقائي انعكاس
    /// لحالة الحضور، فأي تعديل يدوي عليه بيختفي لوحده أول حفظ للحضور —
    /// وتعديل بيختفي من غير ما حد ياخد باله أسوأ من تعديل ممنوع.
    /// </summary>
    public class PenaltyGuardTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const string Password = "5555";

        private static DateTime Today => TestDatabase.Today;

        private async Task SetPasswordAsync()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        /// <summary>بيسجّل جزاء يدوي ويرجّع رقمه</summary>
        private async Task<int> AddManualAsync(string reason = "شرب سجاير في الورشة")
        {
            using var scope = _db.CreateScope();
            var penalty = await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                TestDatabase.WorkerAhmedId, Today, reason, PenaltyDeduction.HalfDay,
                operationsPassword: Password);

            return penalty.Id;
        }

        /// <summary>
        /// بيولّد جزاء تلقائي عن طريق تسجيل غياب بدون إذن — نفس الطريق
        /// اللي التطبيق بيمشي فيه، مش بكتابة صف في الداتابيز بالإيد
        /// </summary>
        private async Task<int> AddAutoAsync()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(
                Today,
                new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission) },
                Password);

            var db = _db.GetService<AppDbContext>(scope);
            var auto = await db.Penalties.SingleAsync(p => p.Source == PenaltySource.AutoAbsence);
            return auto.Id;
        }

        // ======================= بوابة كلمة السر =======================

        [Fact]
        public async Task Recording_a_manual_penalty_needs_the_password()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerAhmedId, Today, "سبب", PenaltyDeduction.OneDay,
                    operationsPassword: "غلط"));

            var db = _db.GetService<AppDbContext>(scope);
            Assert.Empty(await db.Penalties.ToListAsync());
        }

        [Fact]
        public async Task An_auto_penalty_does_not_ask_for_the_password_again()
        {
            // بيتولّد جوه حفظ الحضور اللي عدّى على البوابة خلاص — سؤالها
            // تاني كان هيبقى سؤالين على نفس العملية
            await SetPasswordAsync();

            var autoId = await AddAutoAsync();
            Assert.True(autoId > 0);
        }

        [Fact]
        public async Task Editing_a_penalty_needs_the_password()
        {
            await SetPasswordAsync();
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                    id, "سبب جديد", PenaltyDeduction.OneWeek, operationsPassword: "غلط"));

            // الجزاء زي ما هو
            var db = _db.GetService<AppDbContext>(scope);
            var penalty = await db.Penalties.SingleAsync(p => p.Id == id);
            Assert.Equal("شرب سجاير في الورشة", penalty.Reason);
            Assert.Equal(PenaltyDeduction.HalfDay, penalty.Deduction);
        }

        // ======================= التعديل =======================

        [Fact]
        public async Task Editing_a_manual_penalty_updates_reason_and_deduction()
        {
            await SetPasswordAsync();
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                id, "تأخير متكرر", PenaltyDeduction.OneDay, operationsPassword: Password);

            var db = _db.GetService<AppDbContext>(scope);
            var penalty = await db.Penalties.SingleAsync(p => p.Id == id);

            Assert.Equal("تأخير متكرر", penalty.Reason);
            Assert.Equal(PenaltyDeduction.OneDay, penalty.Deduction);
        }

        [Fact]
        public async Task Editing_never_changes_the_source_tag()
        {
            // لو التعديل غيّر المصدر، الجزاء اليدوي ممكن يتحوّل لتلقائي
            // (فيتشال لوحده) أو العكس — والتفرقة في سجل المراجعة بتضيع
            await SetPasswordAsync();
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                id, "سبب تاني", PenaltyDeduction.ThreeDays, operationsPassword: Password);

            var db = _db.GetService<AppDbContext>(scope);
            var penalty = await db.Penalties.SingleAsync(p => p.Id == id);

            Assert.Equal(PenaltySource.Manual, penalty.Source);
        }

        [Fact]
        public async Task An_empty_reason_is_refused()
        {
            await SetPasswordAsync();
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                    id, "   ", PenaltyDeduction.OneDay, operationsPassword: Password));
        }

        // ======================= حماية الجزاء التلقائي =======================

        [Fact]
        public async Task An_auto_penalty_cannot_be_edited_by_hand()
        {
            await SetPasswordAsync();
            var autoId = await AddAutoAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                    autoId, "سبب بالإيد", PenaltyDeduction.OneWeek, operationsPassword: Password));

            // الرسالة لازم تقول للمستخدم يعمل إيه بدل ما يفضل يجرّب
            Assert.Contains("الحضور والغياب", ex.Message);
        }

        [Fact]
        public async Task An_auto_penalty_cannot_be_deleted_by_hand()
        {
            await SetPasswordAsync();
            var autoId = await AddAutoAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).RemovePenaltyAsync(autoId));
        }

        [Fact]
        public async Task Manual_and_auto_penalties_live_side_by_side_without_mixing()
        {
            await SetPasswordAsync();
            await AddAutoAsync();
            await AddManualAsync("لبس هاندفري");

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var penalties = await db.Penalties.ToListAsync();

            Assert.Equal(2, penalties.Count);
            Assert.Single(penalties, p => p.Source == PenaltySource.Manual);
            Assert.Single(penalties, p => p.Source == PenaltySource.AutoAbsence);
        }
    }
}
