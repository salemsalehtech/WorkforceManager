using WorkforceManager.Business.Services;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// ComparisonText بيستخدم نفس مقارن النسبة العام في البرنامج
    /// (ReportBuilderService.PercentChange)، مفيش حساب نسبة تاني مكرر —
    /// شوف CLAUDE.md.
    /// </summary>
    public class MonthlyPlanComparisonTextTests
    {
        [Fact]
        public void MatchesReportBuilderServicePercentChange_WhenIncreased()
        {
            var row = new MonthlyPlanProductRow { EffectiveAchieved = 130, SameDayPreviousMonth = 100 };

            var expected = ReportBuilderService.PercentChange(130, 100)!.Value;
            Assert.Equal($"▲ {expected:0.#}%", row.ComparisonText);
        }

        [Fact]
        public void MatchesReportBuilderServicePercentChange_WhenDecreased()
        {
            var row = new MonthlyPlanProductRow { EffectiveAchieved = 70, SameDayPreviousMonth = 100 };

            var expected = ReportBuilderService.PercentChange(70, 100)!.Value;
            Assert.Equal($"▼ {Math.Abs(expected):0.#}%", row.ComparisonText);
        }

        [Fact]
        public void Empty_WhenNoPreviousMonthData()
        {
            var row = new MonthlyPlanProductRow { EffectiveAchieved = 70, SameDayPreviousMonth = null };
            Assert.Equal("", row.ComparisonText);
        }

        [Fact]
        public void Empty_WhenPreviousMonthWasZero()
        {
            var row = new MonthlyPlanProductRow { EffectiveAchieved = 70, SameDayPreviousMonth = 0 };
            Assert.Equal("", row.ComparisonText);
        }
    }
}
