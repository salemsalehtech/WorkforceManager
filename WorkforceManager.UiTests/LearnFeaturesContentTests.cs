using WorkforceManager.UI.Tour;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// LearnFeaturesContent.ShouldOffer قرار نقي (بدون WPF ولا إعدادات) —
    /// عشان كده هنا في WorkforceManager.UiTests مباشرة من غير [Collection("WPF")]،
    /// مش محتاج WpfThread. App.OfferLearnFeaturesIfNew بينادي عليها بدل
    /// ما يكرر نفس المقارنة، فاختبار الدالة دي كفاية لتغطية منطق "هل نعرض
    /// تعلم مميزات التحديث؟" بالكامل من غير ما نحتاج نشغّل نافذة حقيقية.
    /// </summary>
    public class LearnFeaturesContentTests
    {
        [Fact]
        public void يعرض_لما_مفيش_تسجيل_سابق_ومحتوى_موجود_للإصدار_الحالي()
        {
            var currentVersion = LearnFeaturesContent.Versions[0].Version;

            Assert.True(LearnFeaturesContent.ShouldOffer(lastSeenLearnVersion: null, currentVersion));
        }

        [Fact]
        public void ميعرضش_لو_آخر_تسجيل_نفس_الإصدار_الحالي_بالظبط()
        {
            var currentVersion = LearnFeaturesContent.Versions[0].Version;

            Assert.False(LearnFeaturesContent.ShouldOffer(currentVersion, currentVersion));
        }

        [Fact]
        public void ميعرضش_لو_مفيش_محتوى_مكتوب_للإصدار_الحالي_أصلًا()
        {
            Assert.False(LearnFeaturesContent.ShouldOffer(
                lastSeenLearnVersion: null, currentAppVersion: "9999.0.0-غير-موجود"));
        }

        [Fact]
        public void يعرض_لو_آخر_تسجيل_إصدار_أقدم_من_الحالي()
        {
            var currentVersion = LearnFeaturesContent.Versions[0].Version;

            Assert.True(LearnFeaturesContent.ShouldOffer("0.0.1", currentVersion));
        }
    }
}
