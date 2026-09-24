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
    }
}
