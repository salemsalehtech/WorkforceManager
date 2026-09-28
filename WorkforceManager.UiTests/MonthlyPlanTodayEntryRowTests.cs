using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// تحذير الاختلاف بين الرقم اليدوي والإنتاج الحقيقي — غير مانع للحفظ،
    /// شوف MonthlyPlanTodayEntryRow.HasMismatchWarning وCLAUDE.md.
    /// </summary>
    public class MonthlyPlanTodayEntryRowTests
    {
        private static MonthlyPlanTodayEntryRow Row(int realProduction) => new()
        { ProductId = 1, ProductName = "منتج", RealProductionToday = realProduction };

        [Fact]
        public void NoWarning_WhenManualMatchesReal()
        {
            var row = Row(realProduction: 100);
            row.QuantityText = "100";

            Assert.False(row.HasMismatchWarning);
        }

        [Fact]
        public void NoWarning_WhenDifferenceWithinThreshold()
        {
            var row = Row(realProduction: 100);
            row.QuantityText = "110"; // 10% فرق — تحت الـ15%

            Assert.False(row.HasMismatchWarning);
        }

        [Fact]
        public void Warning_WhenDifferenceExceedsThreshold()
        {
            var row = Row(realProduction: 100);
            row.QuantityText = "130"; // 30% فرق — فوق الـ15%

            Assert.True(row.HasMismatchWarning);
        }

        [Fact]
        public void Warning_WhenRealIsZeroButManualIsNot()
        {
            var row = Row(realProduction: 0);
            row.QuantityText = "20";

            Assert.True(row.HasMismatchWarning);
        }

        [Fact]
        public void NoWarning_WhenBothRealAndManualAreZero()
        {
            var row = Row(realProduction: 0);
            row.QuantityText = "0";

            Assert.False(row.HasMismatchWarning);
        }

        [Fact]
        public void NoWarning_WhenQuantityTextIsEmpty()
        {
            var row = Row(realProduction: 100);
            row.QuantityText = "";

            Assert.False(row.HasMismatchWarning);
        }
    }
}
