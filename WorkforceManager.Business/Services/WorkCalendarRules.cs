using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// أيام الشغل في شهر — الجمعة مستبعدة دايمًا (زي أسبوع الشغل في باقي
    /// البرنامج، شوف WeeklySummaryService)، زائد عطل يدوية إضافية
    /// (MonthlyWorkCalendarHoliday) بيضيفها المستخدم. نقية بالكامل —
    /// بتاخد قايمة العطل جاهزة، مفيش استعلام قاعدة بيانات هنا.
    /// </summary>
    public static class WorkCalendarRules
    {
        public static bool IsWorkday(DateTime date, IReadOnlySet<DateTime> holidays) =>
            date.DayOfWeek != DayOfWeek.Friday && !holidays.Contains(date.Date);

        /// <summary>عدد أيام الشغل في الشهر كله</summary>
        public static int TotalWorkdays(int year, int month, IReadOnlySet<DateTime> holidays)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var count = 0;
            for (var day = 1; day <= daysInMonth; day++)
                if (IsWorkday(new DateTime(year, month, day), holidays)) count++;
            return count;
        }

        /// <summary>
        /// عدد أيام الشغل من أول الشهر لحد asOfDate شامل — أساس نسبة
        /// المحقق (خطة الشهر × المنقضي ÷ الكلي).
        /// </summary>
        public static int ElapsedWorkdays(int year, int month, DateTime asOfDate, IReadOnlySet<DateTime> holidays)
        {
            var lastDay = Math.Min(asOfDate.Day, DateTime.DaysInMonth(year, month));
            var count = 0;
            for (var day = 1; day <= lastDay; day++)
                if (IsWorkday(new DateTime(year, month, day), holidays)) count++;
            return count;
        }

        public static int RemainingWorkdays(int year, int month, DateTime asOfDate, IReadOnlySet<DateTime> holidays) =>
            TotalWorkdays(year, month, holidays) - ElapsedWorkdays(year, month, asOfDate, holidays);
    }
}
