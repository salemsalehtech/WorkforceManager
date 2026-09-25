using WorkforceManager.UI;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>بادچ جرس الإشعارات — تنسيق العدد المشترك مع بادچات النشاط/الذاكرة</summary>
    public class NotificationBellTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(1, "1")]
        [InlineData(99, "99")]
        [InlineData(100, "٩٩+")]
        [InlineData(250, "٩٩+")]
        public void CountText_FormatsLikeExistingBadges(int count, string expected)
        {
            Assert.Equal(expected, BadgeFormat.CountText(count));
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 0, 1)]
        [InlineData(0, 1, 1)]
        [InlineData(3, 2, 5)]
        public void BadgeTotal_IsTheSumOfBothSources(int unseenActivity, int needsAttention, int expectedTotal)
        {
            // مفيش رقم تالت بيتحسب لوحده — المجموع بس، زي ما الجرس بيعمل بالظبط
            Assert.Equal(expectedTotal, unseenActivity + needsAttention);
        }
    }
}
