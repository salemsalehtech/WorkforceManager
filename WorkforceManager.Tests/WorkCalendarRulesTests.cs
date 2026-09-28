using System;
using System.Collections.Generic;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    public class WorkCalendarRulesTests
    {
        private static readonly IReadOnlySet<DateTime> NoHolidays = new HashSet<DateTime>();

        // سبتمبر 2026: 30 يوم، فيه 4 جُمَع (4، 11، 18، 25) → 26 يوم شغل
        [Fact]
        public void TotalWorkdays_excludes_all_fridays_in_month()
        {
            Assert.Equal(26, WorkCalendarRules.TotalWorkdays(2026, 9, NoHolidays));
        }

        [Fact]
        public void TotalWorkdays_also_excludes_manual_holiday()
        {
            var holidays = new HashSet<DateTime> { new(2026, 9, 15) }; // ثلاثاء، مش جمعة أصلاً
            Assert.Equal(25, WorkCalendarRules.TotalWorkdays(2026, 9, holidays));
        }

        [Fact]
        public void Holiday_that_falls_on_friday_is_not_double_counted()
        {
            var holidays = new HashSet<DateTime> { new(2026, 9, 4) }; // جمعة أصلاً
            Assert.Equal(26, WorkCalendarRules.TotalWorkdays(2026, 9, holidays)); // نفس الرقم من غيرها
        }

        [Fact]
        public void ElapsedWorkdays_counts_up_to_and_including_asOfDate()
        {
            // من 1 لحد 4 سبتمبر (شامل) — 4 أيام، منهم الجمعة (4) مستبعدة → 3
            Assert.Equal(3, WorkCalendarRules.ElapsedWorkdays(2026, 9, new DateTime(2026, 9, 4), NoHolidays));
        }

        [Fact]
        public void RemainingWorkdays_equals_total_minus_elapsed()
        {
            var total = WorkCalendarRules.TotalWorkdays(2026, 9, NoHolidays);
            var elapsed = WorkCalendarRules.ElapsedWorkdays(2026, 9, new DateTime(2026, 9, 15), NoHolidays);
            var remaining = WorkCalendarRules.RemainingWorkdays(2026, 9, new DateTime(2026, 9, 15), NoHolidays);
            Assert.Equal(total - elapsed, remaining);
        }

        [Fact]
        public void IsWorkday_false_on_friday_true_otherwise()
        {
            Assert.False(WorkCalendarRules.IsWorkday(new DateTime(2026, 9, 4), NoHolidays));
            Assert.True(WorkCalendarRules.IsWorkday(new DateTime(2026, 9, 5), NoHolidays));
        }

        // ═══════════ نفس المنطق، بس لمدى تاريخ صريح (start/end) — أساس PlanPeriod ═══════════

        [Fact]
        public void TotalWorkdays_range_excludes_fridays_within_the_range_only()
        {
            // 1→15 سبتمبر 2026 (شامل) = 15 يوم، فيهم جمعتين (4، 11) → 13
            Assert.Equal(13, WorkCalendarRules.TotalWorkdays(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 15), NoHolidays));
        }

        [Fact]
        public void TotalWorkdays_range_also_excludes_manual_holiday_inside_range()
        {
            var holidays = new HashSet<DateTime> { new(2026, 9, 15) }; // ثلاثاء، آخر يوم في المدى
            Assert.Equal(12, WorkCalendarRules.TotalWorkdays(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 15), holidays));
        }

        [Fact]
        public void TotalWorkdays_range_ignores_holiday_outside_range()
        {
            var holidays = new HashSet<DateTime> { new(2026, 9, 20) }; // برّه المدى
            Assert.Equal(13, WorkCalendarRules.TotalWorkdays(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 15), holidays));
        }

        [Fact]
        public void ElapsedWorkdays_range_counts_up_to_and_including_asOfDate()
        {
            // من 1 سبتمبر لحد 30، بس محسوب لحد يوم 15 بس — نفس نتيجة TotalWorkdays لمدى 1-15
            Assert.Equal(13, WorkCalendarRules.ElapsedWorkdays(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), new DateTime(2026, 9, 15), NoHolidays));
        }

        [Fact]
        public void ElapsedWorkdays_range_clamps_to_zero_before_start()
        {
            Assert.Equal(0, WorkCalendarRules.ElapsedWorkdays(
                new DateTime(2026, 9, 10), new DateTime(2026, 9, 20), new DateTime(2026, 9, 5), NoHolidays));
        }

        [Fact]
        public void ElapsedWorkdays_range_clamps_to_total_after_end()
        {
            var total = WorkCalendarRules.TotalWorkdays(new DateTime(2026, 9, 1), new DateTime(2026, 9, 15), NoHolidays);
            Assert.Equal(total, WorkCalendarRules.ElapsedWorkdays(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 15), new DateTime(2026, 12, 1), NoHolidays));
        }

        [Fact]
        public void RemainingWorkdays_range_equals_total_minus_elapsed()
        {
            var start = new DateTime(2026, 9, 1);
            var end = new DateTime(2026, 9, 30);
            var asOf = new DateTime(2026, 9, 15);
            var total = WorkCalendarRules.TotalWorkdays(start, end, NoHolidays);
            var elapsed = WorkCalendarRules.ElapsedWorkdays(start, end, asOf, NoHolidays);
            var remaining = WorkCalendarRules.RemainingWorkdays(start, end, asOf, NoHolidays);
            Assert.Equal(total - elapsed, remaining);
        }
    }
}
