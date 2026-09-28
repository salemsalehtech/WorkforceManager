using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// الإنتاج اليومي المطلوب على مستوى العيلة — صيغة واحدة على إجمالي
    /// العيلة، مش مجموع أرقام منتجات مقرّبة كل واحدة لوحدها (الفرق عن باگ
    /// التقريب/التوزيع في الشيت القديم)، شوف MonthlyPlanFamilyGroupRow.
    /// </summary>
    public class MonthlyPlanFamilyRequiredOutputTests
    {
        private static MonthlyPlanProductRow Product(string quantity) => new() { QuantityText = quantity };

        [Fact]
        public void ComputedDirectlyOnFamilyTotal_NotSumOfPerProductRoundedValues()
        {
            // منتجين، كل واحد لوحده هيقرّب لأعلى (باگ الشيت القديم): 10/3=3.33→4 و10/3=3.33→4 يبقى 8
            // بس على مستوى العيلة: (20 خطة - 0 محقق) / 3 أيام = 6.67 → 7 بالظبط، مش 8
            var family = new MonthlyPlanFamilyGroupRow
            {
                Products = new() { Product("10"), Product("10") }, // Subtotal = 20, AchievedSubtotal = 0
                RemainingWorkdays = 3
            };

            Assert.Equal(7, family.RequiredDailyOutputForFamily);
        }

        [Fact]
        public void ReturnsNull_WhenNoWorkdaysRemain()
        {
            var family = new MonthlyPlanFamilyGroupRow
            {
                Products = new() { Product("100") },
                RemainingWorkdays = 0
            };

            Assert.Null(family.RequiredDailyOutputForFamily);
        }

        [Fact]
        public void ReturnsZero_NotNegative_WhenAlreadyAheadOfPlan()
        {
            var achieved = new MonthlyPlanProductRow { QuantityText = "10" };
            achieved.EffectiveAchieved = 50; // فوق الخطة بكتير

            var family = new MonthlyPlanFamilyGroupRow
            {
                Products = new() { achieved },
                RemainingWorkdays = 5
            };

            Assert.Equal(0, family.RequiredDailyOutputForFamily);
        }
    }
}
