using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// النيات الجديدة في البحث الذكي (هالك، المتأخر في الخطة، الحضور، إنتاج الفترة) — تفكيك العبارة بس
    /// (ParseIntent منطق نقي)، بتاريخ ثابت عشان "امبارح/النهارده" تبقى حتمية.
    /// </summary>
    public class SearchIntentNewKindsTests
    {
        private static readonly DateTime Today = new(2026, 10, 22);

        [Theory]
        [InlineData("هالك")]
        [InlineData("الهالك النهارده")]
        public void Scrap_without_a_day_word_or_with_today_is_a_scrap_question(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query, Today);

            Assert.NotNull(parsed);
            Assert.Equal(SearchIntentKind.Scrap, parsed!.Kind);
        }

        [Fact]
        public void Scrap_yesterday_resolves_to_the_actual_date()
        {
            var parsed = SearchIntentService.ParseIntent("هالك امبارح", Today);

            Assert.Equal(SearchIntentKind.Scrap, parsed!.Kind);
            Assert.Equal(Today.AddDays(-1), parsed.SpecificDay);
        }

        [Theory]
        [InlineData("المتأخر في الخطة")]
        [InlineData("متأخرين")]
        [InlineData("المتاخر")]
        public void Behind_plan_phrasings_all_ask_for_behind_products(string query)
        {
            var parsed = SearchIntentService.ParseIntent(query, Today);

            Assert.Equal(SearchIntentKind.PlanBehind, parsed!.Kind);
        }

        [Fact]
        public void Attendance_without_a_name_is_the_whole_factory_for_the_day()
        {
            var parsed = SearchIntentService.ParseIntent("حضور النهارده", Today);

            Assert.Equal(SearchIntentKind.DayAttendance, parsed!.Kind);
            Assert.Equal(Today, parsed.SpecificDay);
        }

        [Fact]
        public void Attendance_with_a_name_is_that_workers_attendance_answer()
        {
            var parsed = SearchIntentService.ParseIntent("حضور أحمد", Today);

            Assert.Equal(SearchIntentKind.Absence, parsed!.Kind);
            Assert.Equal("احمد", parsed.CandidateName);
        }

        [Fact]
        public void Absence_yesterday_is_the_factory_absence_for_that_day_not_a_worker_named_yesterday()
        {
            var parsed = SearchIntentService.ParseIntent("غياب امبارح", Today);

            Assert.Equal(SearchIntentKind.DayAbsence, parsed!.Kind);
            Assert.Equal(Today.AddDays(-1), parsed.SpecificDay);
        }

        [Fact]
        public void Production_with_only_a_period_word_is_the_factory_total_for_the_period()
        {
            var parsed = SearchIntentService.ParseIntent("إنتاج الشهر", Today);

            Assert.Equal(SearchIntentKind.PeriodProduction, parsed!.Kind);
            Assert.Equal(ReportPeriodKind.ThisMonth, parsed.Period);
        }

        [Fact]
        public void Production_with_a_name_is_still_a_named_production_question()
        {
            var parsed = SearchIntentService.ParseIntent("إنتاج دبلة الشهر", Today);

            Assert.Equal(SearchIntentKind.Production, parsed!.Kind);
            Assert.Equal("دبله", parsed.CandidateName);
        }
    }
}
