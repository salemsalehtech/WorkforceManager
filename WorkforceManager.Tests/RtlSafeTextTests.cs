using WorkforceManager.Core.Helpers;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// النجوم والنسب دي أرقام/رموز بس (مفيش حرف عربي قوي الاتجاه جواها)،
    /// فلو اتعرضت زي ما هي جوه شاشة RTL، محرك الـ Bidi بيقلب ترتيبها
    /// بصريًا: "★★★★☆" بيبان "☆★★★★"، و"11 / 12" بيبان "12 / 11".
    /// الاختبارات دي بتتأكد إن النص المتولّد فعلًا معزول (محاط بعلامتي
    /// Unicode Isolate)، مش إن الشكل "يبان صح" — ده حاجة الشاشة نفسها
    /// بتضمنها بمجرد وجود العزل.
    /// </summary>
    public class RtlSafeTextTests
    {
        private const char LeftToRightIsolate = '⁦';
        private const char PopDirectionalIsolate = '⁩';

        [Fact]
        public void Stars_WrapsTheStarSequence_InAnIsolate()
        {
            var text = RtlSafeText.Stars(3);

            Assert.Equal(LeftToRightIsolate, text[0]);
            Assert.Equal(PopDirectionalIsolate, text[^1]);
            Assert.Contains("★★★☆☆", text);
        }

        [Fact]
        public void Stars_KeepsFilledStarsBeforeEmptyOnes_RegardlessOfIsolation()
        {
            var text = RtlSafeText.Stars(2, total: 5);

            Assert.Equal($"{LeftToRightIsolate}★★☆☆☆{PopDirectionalIsolate}", text);
        }

        [Fact]
        public void Ratio_WrapsTheNumeratorAndDenominator_InAnIsolate()
        {
            var text = RtlSafeText.Ratio(11, 12);

            Assert.Equal($"{LeftToRightIsolate}11 / 12{PopDirectionalIsolate}", text);
        }
    }
}
