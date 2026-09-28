using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// ترتيب تبويب "الإنتاج اليومي" — غير المُدخل يطلع فوق، بمعزل تام عن
    /// أي حالة إنتاج حقيقي (قاعدة هندسية صريحة من الطلب). دالة نقية
    /// (MonthlyPlanViewModel.OrderTodayEntryRows)، مفيش قاعدة بيانات.
    /// </summary>
    public class MonthlyPlanTodayEntryOrderingTests
    {
        private static MonthlyPlanTodayEntryRow Row(string name, bool isFilled, int realProduction = 0) => new()
        {
            ProductId = name.GetHashCode(), ProductName = name, IsFilled = isFilled, RealProductionToday = realProduction
        };

        [Fact]
        public void UnfilledProducts_SortBeforeFilledOnes()
        {
            var filled = Row("أ", isFilled: true);
            var unfilled = Row("ب", isFilled: false);

            var result = MonthlyPlanViewModel.OrderTodayEntryRows(new[] { filled, unfilled });

            Assert.Same(unfilled, result[0]);
            Assert.Same(filled, result[1]);
        }

        [Fact]
        public void UnfilledProductWithRealProduction_StillSortsAsUnfilled()
        {
            // قاعدة صريحة: الترتيب بيعتمد على وجود إدخال يدوي بس، مش على
            // أي نشاط إنتاج حقيقي — منتج له إنتاج حقيقي كبير بس من غير
            // إدخال يدوي لسه "غير مُدخل"
            var filledNoRealProduction = Row("أ", isFilled: true, realProduction: 0);
            var unfilledWithRealProduction = Row("ب", isFilled: false, realProduction: 999);

            var result = MonthlyPlanViewModel.OrderTodayEntryRows(new[] { filledNoRealProduction, unfilledWithRealProduction });

            Assert.Same(unfilledWithRealProduction, result[0]);
        }

        [Fact]
        public void WithinSameFilledState_SortsByName()
        {
            var b = Row("ب", isFilled: false);
            var a = Row("أ", isFilled: false);

            var result = MonthlyPlanViewModel.OrderTodayEntryRows(new[] { b, a });

            Assert.Same(a, result[0]);
            Assert.Same(b, result[1]);
        }
    }
}
