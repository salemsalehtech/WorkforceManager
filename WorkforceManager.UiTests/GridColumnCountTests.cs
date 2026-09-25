using System.Globalization;
using WorkforceManager.UI.ViewModels;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// GridColumnWidthConverter دالة نقية (مش محتاجة نافذة ولا DB) — شوف
    /// CLAUDE.md قسم شبكة الكروت لتفاصيل مشكلة "4 أعمدة بدل 5" اللي القيم
    /// هنا بتثبّتها.
    /// </summary>
    public class GridColumnCountTests
    {
        private const double CardMargin = 10;

        [Fact]
        public void VeryNarrowWidth_ClampsToMinColumns()
        {
            Assert.Equal(3, GridColumnWidthConverter.ColumnsFor(300, isSidebarCollapsed: false));
        }

        [Fact]
        public void ZeroWidth_ClampsToMinColumns()
        {
            Assert.Equal(3, GridColumnWidthConverter.ColumnsFor(0, isSidebarCollapsed: false));
        }

        [Fact]
        public void RightBelowBreakpoint_FourColumns()
        {
            // 5 كروت محتاجين 5 × (170 + 10 هامش + 1 أمان) = 905
            Assert.Equal(4, GridColumnWidthConverter.ColumnsFor(904, isSidebarCollapsed: false));
        }

        [Fact]
        public void RightAtBreakpoint_FiveColumns()
        {
            Assert.Equal(5, GridColumnWidthConverter.ColumnsFor(905, isSidebarCollapsed: false));
        }

        [Fact]
        public void NormalWidth_SidebarOpen_FiveColumns()
        {
            Assert.Equal(5, GridColumnWidthConverter.ColumnsFor(1200, isSidebarCollapsed: false));
        }

        [Fact]
        public void NormalWidth_SidebarCollapsed_SixColumns()
        {
            Assert.Equal(6, GridColumnWidthConverter.ColumnsFor(1200, isSidebarCollapsed: true));
        }

        /// <summary>
        /// أصل المشكلة: الكروت كانت بتملا السطر بالظبط، والتقريب لأقرب بكسل
        /// كان بيزوّد المجموع كسر بكسل ويلفّ آخر كارت. السطر لازم يسيب ولو
        /// بكسل منطقي لكل كارت فاضي — أي عرض من أول ما 3 أعمدة تتسع (543).
        /// </summary>
        [Theory]
        [InlineData(543, false)]
        [InlineData(824, false)]
        [InlineData(905, false)]
        [InlineData(939, false)]
        [InlineData(1105, true)]
        [InlineData(1244, false)]
        [InlineData(1244, true)]
        [InlineData(1496, false)]
        [InlineData(1715, true)]
        public void FullRow_LeavesRoundingSlack(double containerWidth, bool isSidebarCollapsed)
        {
            var converter = new GridColumnWidthConverter();
            var cardWidth = (double)converter.Convert(
                new object[] { containerWidth, isSidebarCollapsed }, typeof(double), null!, CultureInfo.InvariantCulture);
            var columns = GridColumnWidthConverter.ColumnsFor(containerWidth, isSidebarCollapsed);

            var rowWidth = columns * (cardWidth + CardMargin);
            Assert.True(rowWidth <= containerWidth - columns,
                $"row {rowWidth} leaves less than {columns}px slack in {containerWidth}");
        }
    }
}
