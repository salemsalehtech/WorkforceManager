using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// قواعد شاشة الرئيسية النقية (HomeDashboardRules) — من غير قاعدة بيانات.
    /// أهم حاجة هنا "الأقل أداءً": لازم مايسمّيش حد إلا لو الترتيب فيه
    /// عدد كفاية إن آخر واحد مايبقاش في نفس الوقت من "أحسن 3".
    /// </summary>
    public class HomeDashboardRulesTests
    {
        // الأربع — نفس يوم TestDatabase.Today؛ الجمعة اللي قبله 24/7
        private static readonly DateTime Today = TestDatabase.Today;

        private static WorkerWeeklySummaryDto Worker(int id, string name) =>
            new() { WorkerId = id, WorkerName = name };

        private static ProductActivityDto Product(int id, string name, int completed, int stageWork) =>
            new() { ProductId = id, ProductName = name, CompletedPieces = completed, StageWorkPieces = stageWork };

        private static ProductionMemoryDto Memory(int id, DateTime remindOn, DateTime? completedAt = null) =>
            new() { Id = id, ProductName = $"منتج {id}", RemindOn = remindOn, CompletedAt = completedAt };

        // ═══════════ أسهم المقارنة ═══════════

        [Fact]
        public void Trend_is_null_when_previous_week_is_zero()
        {
            // أول أسبوع في تثبيت جديد: مفيش سهم، مش "▲100%"
            Assert.Null(HomeDashboardRules.Trend(40, 0));
            Assert.Null(HomeDashboardRules.Trend(0, 0));
        }

        [Theory]
        [InlineData(115, 100, 15)]
        [InlineData(80, 100, -20)]
        [InlineData(100, 100, 0)]
        [InlineData(0, 50, -100)]
        [InlineData(2, 3, -33)]
        public void Trend_matches_report_percent_change_rounded(int now, int previous, int expected) =>
            Assert.Equal(expected, HomeDashboardRules.Trend(now, previous));

        // ═══════════ الأقل أداءً ═══════════

        [Fact]
        public void Worst_worker_is_null_when_fewer_than_four_are_ranked()
        {
            var ranked = new List<WorkerWeeklySummaryDto> { Worker(1, "أ"), Worker(2, "ب"), Worker(3, "ج") };

            Assert.Null(HomeDashboardRules.PickWorstWorker(ranked));
        }

        [Fact]
        public void Worst_worker_is_the_last_ranked_once_four_are_ranked()
        {
            var ranked = new List<WorkerWeeklySummaryDto> { Worker(1, "أ"), Worker(2, "ب"), Worker(3, "ج"), Worker(4, "د") };

            Assert.Equal(4, HomeDashboardRules.PickWorstWorker(ranked)!.WorkerId);
        }

        [Fact]
        public void Worst_worker_on_an_empty_ranking_is_null()
        {
            Assert.Null(HomeDashboardRules.PickWorstWorker(new List<WorkerWeeklySummaryDto>()));
        }

        // ═══════════ أكتر/أقل منتج ═══════════

        [Fact]
        public void Top_product_ignores_products_with_no_work_and_breaks_ties_by_name()
        {
            var products = new[]
            {
                Product(1, "شنطة", 50, 60),
                Product(2, "دبلة", 50, 70),
                Product(3, "سلسلة", 0, 0) // مفيش شغل خالص
            };

            Assert.Equal("دبلة", HomeDashboardRules.PickTopProduct(products)!.ProductName);
        }

        [Fact]
        public void Bottom_product_is_the_least_completed_among_products_that_actually_worked()
        {
            var products = new[]
            {
                Product(1, "دبلة", 90, 90),
                Product(2, "شنطة", 10, 40),
                Product(3, "سلسلة", 0, 0) // صفر لأنه ماشتغلش، مش "أقل منتج"
            };

            Assert.Equal("شنطة", HomeDashboardRules.PickBottomProduct(products)!.ProductName);
        }

        [Fact]
        public void Bottom_product_is_null_when_only_one_product_worked()
        {
            var products = new[] { Product(1, "دبلة", 90, 90), Product(2, "شنطة", 0, 0) };

            Assert.Null(HomeDashboardRules.PickBottomProduct(products));
        }

        // ═══════════ خطط الذاكرة القريبة ═══════════

        [Fact]
        public void Due_soon_includes_overdue_today_and_the_window_edge_but_not_beyond_it()
        {
            var memories = new[]
            {
                Memory(1, Today.AddDays(-5)),
                Memory(2, Today),
                Memory(3, Today.AddDays(HomeDashboardRules.MemoryDueSoonDays)),
                Memory(4, Today.AddDays(HomeDashboardRules.MemoryDueSoonDays + 1))
            };

            var due = HomeDashboardRules.DueSoon(memories, Today);

            Assert.Equal(new[] { 1, 2, 3 }, due.Select(m => m.Id));
        }

        [Fact]
        public void Due_soon_excludes_completed_plans_and_sorts_nearest_first()
        {
            var memories = new[]
            {
                Memory(1, Today.AddDays(2)),
                Memory(2, Today.AddDays(-1)),
                Memory(3, Today, completedAt: Today)
            };

            var due = HomeDashboardRules.DueSoon(memories, Today);

            Assert.Equal(new[] { 2, 1 }, due.Select(m => m.Id));
        }

        // ═══════════ سلسلة الالتزام ═══════════

        private static (DateTime, AttendanceStatus) Present(DateTime d) => (d, AttendanceStatus.Present);

        private static (DateTime, AttendanceStatus) Absent(DateTime d) => (d, AttendanceStatus.AbsentWithoutPermission);

        [Fact]
        public void Streak_is_zero_and_not_capped_with_no_attendance_at_all()
        {
            var result = HomeDashboardRules.ComputeStreak(Array.Empty<(DateTime, AttendanceStatus)>(), Today);

            Assert.Equal(0, result.Days);
            Assert.False(result.IsCapped);
        }

        [Fact]
        public void Friday_neither_breaks_nor_counts_toward_the_streak()
        {
            // الخميس 23 ← (الجمعة 24 مفيش سجل) ← السبت 25 … الأربع 29
            var records = Enumerable.Range(0, 7)
                .Select(i => Today.AddDays(-i))
                .Where(d => d.DayOfWeek != DayOfWeek.Friday)
                .Select(Present);

            var result = HomeDashboardRules.ComputeStreak(records, Today);

            Assert.Equal(6, result.Days);
        }

        [Fact]
        public void An_absence_recorded_on_a_Friday_is_ignored_like_the_day_itself()
        {
            var friday = Today.AddDays(-5);
            Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);

            var records = new[] { Present(Today), Absent(friday), Present(friday.AddDays(-1)) };

            Assert.Equal(2, HomeDashboardRules.ComputeStreak(records, Today).Days);
        }

        [Fact]
        public void An_unexcused_absence_by_any_worker_breaks_the_streak_even_if_others_were_present()
        {
            var records = new[]
            {
                Present(Today), Present(Today.AddDays(-1)),
                Present(Today.AddDays(-2)), Absent(Today.AddDays(-2)), // نفس اليوم: واحد حاضر وواحد غايب
                Present(Today.AddDays(-3))
            };

            var result = HomeDashboardRules.ComputeStreak(records, Today);

            Assert.Equal(2, result.Days);
            Assert.False(result.IsCapped);
        }

        [Fact]
        public void Excused_absence_does_not_break_the_streak()
        {
            var records = new[]
            {
                Present(Today),
                (Today.AddDays(-1), AttendanceStatus.AbsentWithPermission)
            };

            Assert.Equal(2, HomeDashboardRules.ComputeStreak(records, Today).Days);
        }

        [Fact]
        public void Today_with_no_records_yet_is_skipped_not_treated_as_a_break()
        {
            var records = new[] { Present(Today.AddDays(-1)), Present(Today.AddDays(-2)) };

            Assert.Equal(2, HomeDashboardRules.ComputeStreak(records, Today).Days);
        }

        [Fact]
        public void A_day_with_no_records_at_all_like_a_holiday_is_skipped()
        {
            var records = new[] { Present(Today), Present(Today.AddDays(-2)) }; // الثلاثاء 28 أجازة

            Assert.Equal(2, HomeDashboardRules.ComputeStreak(records, Today).Days);
        }

        [Fact]
        public void An_unbroken_streak_with_history_older_than_the_lookback_is_capped()
        {
            const int lookback = HomeDashboardRules.StreakLookbackDays;
            var records = Enumerable.Range(0, lookback + 5)
                .Select(i => Today.AddDays(-i))
                .Where(d => d.DayOfWeek != DayOfWeek.Friday)
                .Select(Present)
                .ToList();

            var result = HomeDashboardRules.ComputeStreak(records, Today);

            var expectedDays = Enumerable.Range(0, lookback)
                .Count(i => Today.AddDays(-i).DayOfWeek != DayOfWeek.Friday);
            Assert.Equal(expectedDays, result.Days);
            Assert.True(result.IsCapped);
        }

        [Fact]
        public void An_unbroken_streak_on_a_new_install_is_not_capped()
        {
            var records = new[] { Present(Today), Present(Today.AddDays(-1)), Present(Today.AddDays(-2)) };

            var result = HomeDashboardRules.ComputeStreak(records, Today);

            Assert.Equal(3, result.Days);
            Assert.False(result.IsCapped);
        }
    }
}
