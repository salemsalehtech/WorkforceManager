using WorkforceManager.Core.Helpers;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// قاعدة الاسم المختصر على كارت شاشة العمال — أول جزئين بس، مع لحق
    /// بادئات "عبد"/"أبو"/"ابو" بالكلمة اللي بعدها عشان محدش يتقطع في نص
    /// معناه (شوف ShortName.cs).
    /// </summary>
    public class ShortNameTests
    {
        [Fact]
        public void MultiPartName_TakesFirstTwoWordsOnly()
        {
            Assert.Equal("أحمد محمد", ShortName.From("أحمد محمد علي"));
        }

        [Fact]
        public void SingleWordName_ReturnsItselfUnchanged()
        {
            Assert.Equal("أحمد", ShortName.From("أحمد"));
        }

        [Fact]
        public void ExtraSpaces_AreCollapsed()
        {
            Assert.Equal("أحمد محمد", ShortName.From("  أحمد    محمد   علي  "));
        }

        [Fact]
        public void AbdCompoundName_KeepsTheTwoWordsTogetherAsOnePart()
        {
            Assert.Equal("عبد الله محمد", ShortName.From("عبد الله محمد علي"));
        }

        [Fact]
        public void AbdInTheMiddle_StillJoinsWithTheFollowingWord()
        {
            // الجزء الأول "محمد"، والجزء التاني "عبد الرحمن" (ملحوقة) —
            // "أحمد" بعدها بيتقطع لأننا وصلنا لجزئين خلاص
            Assert.Equal("محمد عبد الرحمن", ShortName.From("محمد عبد الرحمن أحمد"));
        }

        [Theory]
        [InlineData("أبو بكر محمد")]
        [InlineData("ابو بكر محمد")]
        public void AboCompoundName_BothSpellingsJoinWithTheFollowingWord(string fullName)
        {
            var expected = fullName.StartsWith("أبو") ? "أبو بكر محمد" : "ابو بكر محمد";
            Assert.Equal(expected, ShortName.From(fullName));
        }

        [Fact]
        public void DanglingCompoundPrefixAtTheEnd_StandsAloneAsItsOwnPart()
        {
            // "عبد" آخر كلمة في الاسم كله — مفيش كلمة بعدها تتلحق بيها
            Assert.Equal("محمد عبد", ShortName.From("محمد عبد"));
        }

        [Fact]
        public void EmptyOrBlankName_ReturnsEmptyString()
        {
            Assert.Equal("", ShortName.From("   "));
            Assert.Equal("", ShortName.From(null));
        }
    }
}
