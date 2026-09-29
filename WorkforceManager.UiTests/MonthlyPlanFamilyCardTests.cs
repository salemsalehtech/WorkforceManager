using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// كارت العيلة الجديد في شاشة الخطة الشهرية — نسبة الإنجاز الذهبية
    /// (CardPercentText) وطي/فتح الكارت (IsExpanded). شوف MonthlyPlanRows.cs.
    /// </summary>
    public class MonthlyPlanFamilyCardTests
    {
        private static MonthlyPlanProductRow Product(int planned, int achieved) => new()
        {
            ProductId = 1, ProductName = "منتج", QuantityText = planned.ToString(),
            EffectiveAchieved = achieved,
            AchievedPercent = planned == 0 ? null : (decimal)achieved / planned
        };

        [Fact]
        public void CardPercentText_ShowsDash_WhenNoProductsHavePercent()
        {
            var family = new MonthlyPlanFamilyGroupRow { HeaderText = "عيلة", Products = new List<MonthlyPlanProductRow> { Product(0, 0) } };
            Assert.Equal("—", family.CardPercentText);
        }

        [Fact]
        public void CardPercentText_ShowsAverageAcrossProducts()
        {
            var family = new MonthlyPlanFamilyGroupRow
            {
                HeaderText = "عيلة",
                Products = new List<MonthlyPlanProductRow> { Product(100, 50), Product(100, 100) } // 50% و100% → متوسط 75%
            };
            Assert.Equal("75%", family.CardPercentText);
        }

        [Fact]
        public void IsExpanded_DefaultsTrue_AndTogglesViaCommand()
        {
            var family = new MonthlyPlanFamilyGroupRow { HeaderText = "عيلة" };
            Assert.True(family.IsExpanded);

            family.ToggleExpandedCommand.Execute(null);
            Assert.False(family.IsExpanded);

            family.ToggleExpandedCommand.Execute(null);
            Assert.True(family.IsExpanded);
        }
    }
}
