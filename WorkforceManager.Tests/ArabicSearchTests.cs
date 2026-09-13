using WorkforceManager.Core.Helpers;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات مطابقة الأسماء العربية في البحث. دي القاعدة اللي بحث
    /// العمال في شاشة التسجيل اليومي قايم عليها، ولو حرف فيها اتظبط غلط
    /// البحث بيرجع فاضي في سكوت — المستخدم هيفتكر إن العامل مش مؤهل
    /// للمرحلة أصلاً بدل ما يعرف إن دي مشكلة كتابة.
    /// </summary>
    public class ArabicSearchTests
    {
        // ---------- الهمزات: أكتر حاجة بتتكتب غلط في الإدخال السريع ----------

        [Theory]
        [InlineData("أحمد", "احمد")]   // همزة فوق ← ألف عادية
        [InlineData("احمد", "أحمد")]   // والعكس
        [InlineData("إبراهيم", "ابراهيم")]
        [InlineData("آمنه", "امنه")]
        public void Hamza_forms_match_plain_alef(string name, string query) =>
            Assert.True(ArabicSearch.Contains(name, query));

        // ---------- التاء المربوطة والألف المقصورة ----------

        [Theory]
        [InlineData("لفة صغيرة", "لفه")]
        [InlineData("لفه صغيره", "لفة")]
        [InlineData("يحيى", "يحيي")]
        [InlineData("مصطفى", "مصطفي")]
        public void Taa_marbuta_and_alef_maqsura_are_interchangeable(string name, string query) =>
            Assert.True(ArabicSearch.Contains(name, query));

        // ---------- التشكيل والتطويل ----------

        [Fact]
        public void Diacritics_are_ignored() =>
            Assert.True(ArabicSearch.Contains("مُحَمَّد", "محمد"));

        [Fact]
        public void Tatweel_is_ignored() =>
            Assert.True(ArabicSearch.Contains("محـــمد", "محمد"));

        // ---------- المطابقة الجزئية: أول كام حرف كفاية ----------

        [Fact]
        public void First_letters_match_start_of_name() =>
            Assert.True(ArabicSearch.Contains("محمد علي حسن", "محم"));

        [Fact]
        public void Query_matches_middle_of_name() =>
            Assert.True(ArabicSearch.Contains("محمد علي حسن", "علي"));

        // ---------- الحالات اللي المفروض ترجع false ----------

        [Fact]
        public void Different_name_does_not_match() =>
            Assert.False(ArabicSearch.Contains("محمد علي", "إبراهيم"));

        [Fact]
        public void Empty_query_matches_everything() =>
            Assert.True(ArabicSearch.Contains("محمد علي", ""));

        [Fact]
        public void Null_name_never_matches_a_real_query() =>
            Assert.False(ArabicSearch.Contains(null, "محمد"));

        // ---------- التطبيع نفسه ----------

        [Fact]
        public void Normalize_collapses_every_variant_to_one_form() =>
            Assert.Equal("اااا", ArabicSearch.Normalize("اأإآ"));

        [Fact]
        public void Normalize_handles_null_and_empty()
        {
            Assert.Equal("", ArabicSearch.Normalize(null));
            Assert.Equal("", ArabicSearch.Normalize(""));
        }

        [Fact]
        public void Normalize_leaves_latin_text_untouched() =>
            Assert.Equal("Ahmed", ArabicSearch.Normalize("Ahmed"));
    }
}
