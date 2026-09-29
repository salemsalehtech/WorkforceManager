using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// كارت العيلة في تبويب "الإنتاج اليومي" — صح أخضر (AllFilled) بس لما
    /// كل منتجاتها تتسجل النهارده. شوف MonthlyPlanTodayEntryRow.cs.
    /// </summary>
    public class MonthlyPlanTodayFamilyGroupRowTests
    {
        private static MonthlyPlanTodayEntryRow Row(bool filled) => new() { ProductId = 1, ProductName = "منتج", IsFilled = filled };

        [Fact]
        public void AllFilled_False_WhenAnyProductNotFilled()
        {
            var family = new MonthlyPlanTodayFamilyGroupRow
            { HeaderText = "عيلة", Products = new List<MonthlyPlanTodayEntryRow> { Row(true), Row(false) } };

            Assert.False(family.AllFilled);
            Assert.Equal(1, family.FilledCount);
            Assert.Equal(2, family.TotalCount);
        }

        [Fact]
        public void AllFilled_True_WhenEveryProductFilled()
        {
            var family = new MonthlyPlanTodayFamilyGroupRow
            { HeaderText = "عيلة", Products = new List<MonthlyPlanTodayEntryRow> { Row(true), Row(true) } };

            Assert.True(family.AllFilled);
            Assert.Equal(2, family.FilledCount);
        }

        [Fact]
        public void AllFilled_False_WhenNoProducts()
        {
            var family = new MonthlyPlanTodayFamilyGroupRow { HeaderText = "عيلة فاضية" };
            Assert.False(family.AllFilled);
        }

        [Fact]
        public void IsExpanded_DefaultsTrue_AndToggles()
        {
            var family = new MonthlyPlanTodayFamilyGroupRow { HeaderText = "عيلة" };
            Assert.True(family.IsExpanded);
            family.ToggleExpandedCommand.Execute(null);
            Assert.False(family.IsExpanded);
        }
    }
}
