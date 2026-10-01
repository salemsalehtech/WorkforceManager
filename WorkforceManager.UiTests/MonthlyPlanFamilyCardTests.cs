using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// كارت العيلة الجديد في شاشة الخطة الشهرية — نسبة الإنجاز الذهبية
    /// (CardPercentText) والإنذار البدري (IsForecastBelowPlan). شوف MonthlyPlanRows.cs.
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
        public void ForecastWarning_FlagsProductsClosingUnder90PercentOfPlan()
        {
            var under = new MonthlyPlanProductRow { QuantityText = "1000", ForecastEndOfMonth = 899, RemainingWorkdays = 5 };
            var edge = new MonthlyPlanProductRow { QuantityText = "1000", ForecastEndOfMonth = 900 };
            var noPlan = new MonthlyPlanProductRow { QuantityText = "0", ForecastEndOfMonth = 10 };

            Assert.True(under.IsForecastBelowPlan);
            Assert.Equal("التوقّع ناقص 101 عن الخطة", under.ForecastShortfallText);
            under.RemainingWorkdays = 0; // الشهر قفل: التوقّع بقى النتيجة
            Assert.Equal("قفل ناقص 101 عن الخطة", under.ForecastShortfallText);
            Assert.False(edge.IsForecastBelowPlan);
            Assert.False(noPlan.IsForecastBelowPlan);

            var family = new MonthlyPlanFamilyGroupRow { Products = new() { under, edge, noPlan } };
            Assert.Equal(1, family.ForecastBelowPlanCount);
        }

        [Fact]
        public void DailyTargetConflict_WhenTargetTimesRemainingDaysMissesThePlan()
        {
            // 400 محقق + 20 × 10 أيام = 600 < خطة 1000 → تعارض
            var row = new MonthlyPlanProductRow
            {
                QuantityText = "1000", EffectiveAchieved = 400, RemainingWorkdays = 10, RequiredDailyOutput = 60, DailyTargetText = "20"
            };
            Assert.True(row.HasDailyTargetConflict);

            row.DailyTargetText = "60"; // 400 + 600 = 1000 — بالظبط
            Assert.False(row.HasDailyTargetConflict);

            row.DailyTargetText = ""; // مفيش هدف يومي = مفيش تعارض
            Assert.False(row.HasDailyTargetConflict);
        }

        [Fact]
        public void ClosedFamily_ShowsResultInsteadOfRequiredDaily()
        {
            var family = new MonthlyPlanFamilyGroupRow
            {
                Products = new() { new MonthlyPlanProductRow { QuantityText = "1000", EffectiveAchieved = 780 } },
                RemainingWorkdays = 0, TotalWorkdays = 26
            };
            Assert.Equal("النتيجة", family.ThirdStatLabel);
            Assert.Equal("78%", family.ThirdStatValue);
        }

        [Fact]
        public void Key_IsStableAcrossProductCountChanges()
        {
            var before = new MonthlyPlanFamilyGroupRow { HeaderText = "كباشات (3)", FamilyName = "كباشات", MaterialName = "نحاس" };
            var after = new MonthlyPlanFamilyGroupRow { HeaderText = "كباشات (4)", FamilyName = "كباشات", MaterialName = "نحاس" };
            Assert.Equal(before.Key, after.Key);
        }
    }
}
