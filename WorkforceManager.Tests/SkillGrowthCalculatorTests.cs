using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// نمو تقييم مهارة اتضافت تلقائيًا — دالة نقية، مفيش قاعدة بيانات
    /// هنا خالص (شوف SkillGrowthCalculator نفسها للصيغة والحدود المتفق
    /// عليها: 2⭐/3 اختيارات، 3⭐/8، 4⭐(سقف)/15).
    /// </summary>
    public class SkillGrowthCalculatorTests
    {
        [Fact]
        public void ManySelections_ButPoorOutput_StaysLow()
        {
            // 20 اختيار (كتير جدًا) بس أداء ضعيف (0.5 = نصف الكوتة) — نجوم
            // الجودة = 1 (أقل باند)، فمفيش نمو مهما كان عدد الاختيارات
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 1, timesSelected: 20, measuredRatio: 0.5m);

            Assert.Equal(1, result);
        }

        [Fact]
        public void FewSelections_ButExcellentOutput_DoesNotJumpEarly()
        {
            // أداء ممتاز (1.5 فوق الكوتة 50%) من أول اختيارين — مفيش نمو
            // لسه، أقل عتبة (2⭐) محتاجة 3 اختيارات على الأقل
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 1, timesSelected: 2, measuredRatio: 1.5m);

            Assert.Equal(1, result);
        }

        [Fact]
        public void ThreeSelections_WithGoodOutput_ReachesTwoStars()
        {
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 1, timesSelected: 3, measuredRatio: 1.5m);

            Assert.Equal(2, result);
        }

        [Fact]
        public void CapHolds_AtFourStars_RegardlessOfHowGoodOrHowMany()
        {
            // 100 اختيار وأداء ممتاز جدًا — برضه مايعديش السقف (5 قرار مدير بس)
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 4, timesSelected: 100, measuredRatio: 3.0m);

            Assert.Equal(SkillGrowthCalculator.AutoGrowthCap, result);
            Assert.True(result < 5);
        }

        [Fact]
        public void NeverReturnsLessThanCurrentStars()
        {
            // أداء ضعيف جدًا (0.1) بعد ما كان وصل 3 نجوم فعلاً — مبينزلش
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 3, timesSelected: 50, measuredRatio: 0.1m);

            Assert.Equal(3, result);
        }

        [Fact]
        public void NoMeasuredRatioYet_NoChangeRegardlessOfSelectionCount()
        {
            var result = SkillGrowthCalculator.ComputeNextStars(currentStars: 1, timesSelected: 50, measuredRatio: null);

            Assert.Equal(1, result);
        }
    }
}
