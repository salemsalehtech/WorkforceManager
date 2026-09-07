using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الجزاءات: التعديل، وحماية الجزاء التلقائي.
    ///
    /// **مفيش بوابة باسورد على تسجيل/تعديل جزاء (Tier B)** — بقى متغطى
    /// بتوقيع نهاية اليوم بدل باسورد فوري (شوف DailyOperationsSignOffServiceTests
    /// لاختبارات البوابة نفسها). أخطر حاجة هنا هي **التفرقة بين اليدوي
    /// والتلقائي**: الجزاء التلقائي انعكاس لحالة الحضور، فأي تعديل يدوي
    /// عليه بيختفي لوحده أول حفظ للحضور — وتعديل بيختفي من غير ما حد
    /// ياخد باله أسوأ من تعديل ممنوع.
    /// </summary>
    public class PenaltyGuardTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today;

        /// <summary>بيسجّل جزاء يدوي ويرجّع رقمه</summary>
        private async Task<int> AddManualAsync(string reason = "شرب سجاير في الورشة")
        {
            using var scope = _db.CreateScope();
            var penalty = await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                TestDatabase.WorkerAhmedId, Today, reason, PenaltyDeduction.HalfDay);

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
                new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission) });

            var db = _db.GetService<AppDbContext>(scope);
            var auto = await db.Penalties.SingleAsync(p => p.Source == PenaltySource.AutoAbsence);
            return auto.Id;
        }

        // ======================= التعديل =======================

        [Fact]
        public async Task Editing_a_manual_penalty_updates_reason_and_deduction()
        {
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                id, "تأخير متكرر", PenaltyDeduction.OneDay);

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
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                id, "سبب تاني", PenaltyDeduction.ThreeDays);

            var db = _db.GetService<AppDbContext>(scope);
            var penalty = await db.Penalties.SingleAsync(p => p.Id == id);

            Assert.Equal(PenaltySource.Manual, penalty.Source);
        }

        [Fact]
        public async Task An_empty_reason_is_refused()
        {
            var id = await AddManualAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                    id, "   ", PenaltyDeduction.OneDay));
        }

        // ======================= حماية الجزاء التلقائي =======================

        [Fact]
        public async Task An_auto_penalty_cannot_be_edited_by_hand()
        {
            var autoId = await AddAutoAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).UpdatePenaltyAsync(
                    autoId, "سبب بالإيد", PenaltyDeduction.OneWeek));

            // الرسالة لازم تقول للمستخدم يعمل إيه بدل ما يفضل يجرّب
            Assert.Contains("الحضور والغياب", ex.Message);
        }

        [Fact]
        public async Task An_auto_penalty_cannot_be_deleted_by_hand()
        {
            var autoId = await AddAutoAsync();

            using var scope = _db.CreateScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<PenaltyService>(scope).RemovePenaltyAsync(autoId));
        }

        [Fact]
        public async Task Manual_and_auto_penalties_live_side_by_side_without_mixing()
        {
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
