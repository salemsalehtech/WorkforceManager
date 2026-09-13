using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تقطيع النطاقات على المراحل اللي عليها عمال.
    ///
    /// القاعدة اللي الاختبارات دي بتحرسها: **اللي اتشتغل يتحفظ، واللي
    /// مااتشتغلش يفضل واقف**. قبلها كان الحفظ بيترفض بالكامل لو مرحلة
    /// واحدة في النطاق مالهاش عمال، فشغل يوم كامل كان بيضيع والمستخدم
    /// يفتكر إن الشغل الواقف بايظ.
    ///
    /// كل مقطع بياخد نفس عدد قطع النطاق الأصلي — مش تقريب: كل مرحلة في
    /// النطاق أصلاً بتاخد نفس الرقم.
    /// </summary>
    public class FlowRangeTrimmerTests
    {
        // خط من 5 مراحل بأرقام واضحة
        private static readonly int[] Line = { 10, 20, 30, 40, 50 };

        private static FlowRangeDto Range(int from, int to, int pieces = 1000) =>
            new() { FromStageId = from, ToStageId = to, PieceCount = pieces };

        private static FlowTrimResult Trim(FlowRangeDto range, params int[] staffed) =>
            FlowRangeTrimmer.Trim(new[] { range }, Line, staffed.ToHashSet());

        [Fact]
        public void EveryStageStaffed_KeepsTheRangeAsOneWholeRange()
        {
            var result = Trim(Range(10, 50), 10, 20, 30, 40, 50);

            Assert.False(result.HasDropped);
            var kept = Assert.Single(result.Ranges);
            Assert.Equal(10, kept.FromStageId);
            Assert.Equal(50, kept.ToStageId);
            Assert.Equal(1000, kept.PieceCount);
        }

        [Fact]
        public void UnstaffedTail_IsTrimmedOff_AndReported()
        {
            // الحالة الحقيقية: المستخدم دوس "كمّل من هنا" فاتعمل نطاق
            // لآخر الخط، وكمّل أول مرحلتين بس النهارده
            var result = Trim(Range(10, 50), 10, 20);

            var kept = Assert.Single(result.Ranges);
            Assert.Equal(10, kept.FromStageId);
            Assert.Equal(20, kept.ToStageId);

            Assert.Equal(new[] { 30, 40, 50 }, result.DroppedStageIds);
        }

        [Fact]
        public void AGapInTheMiddle_SplitsIntoTwoRanges_EachKeepingTheFullPieceCount()
        {
            var result = Trim(Range(10, 50), 10, 20, 40, 50);

            Assert.Equal(2, result.Ranges.Count);

            Assert.Equal(10, result.Ranges[0].FromStageId);
            Assert.Equal(20, result.Ranges[0].ToStageId);
            Assert.Equal(40, result.Ranges[1].FromStageId);
            Assert.Equal(50, result.Ranges[1].ToStageId);

            // كل مقطع بياخد نفس رقم النطاق الأصلي — مش نصّه ولا مقسوم
            Assert.All(result.Ranges, r => Assert.Equal(1000, r.PieceCount));

            Assert.Equal(new[] { 30 }, result.DroppedStageIds);
        }

        [Fact]
        public void NoStageStaffedAtAll_ProducesNoRanges()
        {
            var result = Trim(Range(10, 50));

            Assert.Empty(result.Ranges);
            Assert.Equal(new[] { 10, 20, 30, 40, 50 }, result.DroppedStageIds);
        }

        [Fact]
        public void ASingleStaffedStageInTheMiddle_SurvivesAsAOneStageRange()
        {
            var result = Trim(Range(10, 50), 30);

            var kept = Assert.Single(result.Ranges);
            Assert.Equal(30, kept.FromStageId);
            Assert.Equal(30, kept.ToStageId);
        }

        [Fact]
        public void SeveralRanges_AreTrimmedIndependently()
        {
            var result = FlowRangeTrimmer.Trim(
                new[] { Range(10, 20, 500), Range(30, 50, 800) },
                Line,
                new HashSet<int> { 10, 30 });

            Assert.Equal(2, result.Ranges.Count);
            Assert.Equal(500, result.Ranges[0].PieceCount);
            Assert.Equal(10, result.Ranges[0].ToStageId);
            Assert.Equal(800, result.Ranges[1].PieceCount);
            Assert.Equal(30, result.Ranges[1].ToStageId);

            Assert.Equal(new[] { 20, 40, 50 }, result.DroppedStageIds);
        }

        // ------- حالات بتعدّي زي ما هي عشان الخدمة تشرح غلطها بنفسها -------

        [Fact]
        public void AStageOutsideTheLine_PassesThroughUntouched()
        {
            // مرحلة الرص مثلاً — الخدمة هي اللي ترفضها برسالتها
            var result = FlowRangeTrimmer.Trim(
                new[] { Range(10, 999) }, Line, new HashSet<int> { 10 });

            var kept = Assert.Single(result.Ranges);
            Assert.Equal(999, kept.ToStageId);
            Assert.False(result.HasDropped);
        }

        [Fact]
        public void AReversedRange_PassesThroughUntouched()
        {
            var result = Trim(Range(50, 10), 10, 50);

            var kept = Assert.Single(result.Ranges);
            Assert.Equal(50, kept.FromStageId);
            Assert.Equal(10, kept.ToStageId);
            Assert.False(result.HasDropped);
        }
    }
}
