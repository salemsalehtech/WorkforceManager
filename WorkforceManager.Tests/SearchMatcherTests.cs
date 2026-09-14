using System.Diagnostics;
using WorkforceManager.Core.Helpers;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات محرك المطابقة المتدرّج (البحث الشامل الجديد قايم عليه):
    /// تطابق دقيق، بادئة، احتواء، وأخيرًا تسامح مع خطأ إملائي واحد أو
    /// اتنين. الطبقات الأرخص لازم تمنع الأغلى من الاشتغال إلا لما تفشل
    /// فعلًا — الاختبار الأخير في الملف ده بيثبت الأداء نفسه، مش بس السلوك.
    /// </summary>
    public class SearchMatcherTests
    {
        // ---------- تطابق دقيق / بادئة / احتواء ----------

        [Fact]
        public void Exact_match_after_normalization_wins_top_score() =>
            Assert.Equal(SearchMatchKind.Exact, SearchMatcher.Match("محمد", "محمد")!.Value.Kind);

        [Fact]
        public void Query_matching_start_of_a_word_is_prefix() =>
            Assert.Equal(SearchMatchKind.Prefix, SearchMatcher.Match("عل", "محمد علي حسن")!.Value.Kind);

        [Fact]
        public void Query_inside_a_word_but_not_at_its_start_is_substring() =>
            Assert.Equal(SearchMatchKind.Substring, SearchMatcher.Match("لي", "محمد علي حسن")!.Value.Kind);

        [Fact]
        public void Tiers_rank_exact_above_prefix_above_substring_above_fuzzy()
        {
            var exact = SearchMatcher.Match("محمد", "محمد")!.Value.Score;
            var prefix = SearchMatcher.Match("عل", "محمد علي حسن")!.Value.Score;
            var substring = SearchMatcher.Match("لي", "محمد علي حسن")!.Value.Score;
            var fuzzy = SearchMatcher.Match("اخمد", "احمد")!.Value.Score; // خطأ إملائي واحد

            Assert.True(exact > prefix);
            Assert.True(prefix > substring);
            Assert.True(substring > fuzzy);
        }

        // ---------- التطبيع العربي (همزات/تاء مربوطة/ألف مقصورة) بيوصل هنا كمان ----------

        [Fact]
        public void Hamza_variants_still_match_through_the_matcher() =>
            Assert.Equal(SearchMatchKind.Exact, SearchMatcher.Match("احمد", "أحمد")!.Value.Kind);

        [Fact]
        public void Diacritics_are_ignored_through_the_matcher() =>
            Assert.Equal(SearchMatchKind.Exact, SearchMatcher.Match("محمد", "مُحَمَّد")!.Value.Kind);

        // ---------- المسافات الزيادة/الناقصة ----------

        [Fact]
        public void Extra_spaces_do_not_prevent_a_substring_match() =>
            Assert.Equal(SearchMatchKind.Substring, SearchMatcher.Match("علي حسن", "محمد   علي   حسن")!.Value.Kind);

        // ---------- خطأ إملائي واحد: استبدال حرف ----------

        [Fact]
        public void One_substituted_letter_still_matches_as_fuzzy()
        {
            var result = SearchMatcher.Match("اخمد", "احمد"); // خ بدل ح
            Assert.NotNull(result);
            Assert.Equal(SearchMatchKind.Fuzzy, result!.Value.Kind);
        }

        // ---------- خطأ إملائي واحد: تبديل موضع حرفين متجاورين ----------

        [Fact]
        public void One_transposed_pair_of_letters_still_matches_as_fuzzy()
        {
            var result = SearchMatcher.Match("احدم", "احمد"); // م و د اتبدل مكانهم
            Assert.NotNull(result);
            Assert.Equal(SearchMatchKind.Fuzzy, result!.Value.Kind);
        }

        [Fact]
        public void Transposition_costs_one_step_not_two()
        {
            // لو التبديل اتحسب كاستبدالين (مش خطوة واحدة) المسافة هتبقى 2
            // وهتتصنّف برّه حد الاستعلام القصير (MaxFuzzyDistanceShort = 1)
            Assert.Equal(1, SearchMatcher.BoundedEditDistance("ab", "ba", maxDistance: 2));
        }

        // ---------- مطابقة جزئية عادية ----------

        [Fact]
        public void Partial_word_inside_longer_text_matches() =>
            Assert.NotNull(SearchMatcher.Match("حسن", "محمد علي حسن"));

        // ---------- نتائج فاضية ----------

        [Fact]
        public void Empty_query_matches_nothing() =>
            Assert.Null(SearchMatcher.Match("", "محمد"));

        [Fact]
        public void Empty_text_matches_nothing() =>
            Assert.Null(SearchMatcher.Match("محمد", ""));

        [Fact]
        public void Completely_unrelated_text_matches_nothing() =>
            Assert.Null(SearchMatcher.Match("قطة", "مصنع الإنتاج والتوزيع"));

        // ---------- استعلام أقصر من حد الفَزّي ----------

        [Fact]
        public void Query_shorter_than_the_fuzzy_minimum_never_falls_back_to_fuzzy()
        {
            // "زي" (حرفين) أقصر من MinQueryLengthForFuzzy=3، فمفروض يرجع null
            // حتى لو فيه كلمة قريبة منه، بدل ما يرجّع تطابق فَزّي عشوائي
            Assert.True("زي".Length < SearchMatcher.MinQueryLengthForFuzzy);
            Assert.Null(SearchMatcher.Match("زي", "محمد"));
        }

        // ---------- الأداء: مجموعة بيانات كبيرة من غير ما تتأخر ----------

        [Fact]
        public void Matching_across_tens_of_thousands_of_rows_stays_fast()
        {
            var rows = new string[40_000];
            for (var i = 0; i < rows.Length; i++)
                rows[i] = $"عامل رقم {i} - مرحلة تشطيب ومراجعة الجودة";

            // استعلام بخطأ إملائي واحد عشان نجبر أغلب الصفوف تكمل للطبقة
            // الرخيصة الأولى (احتواء) وترفضها، بدل ما توقف بتطابق مبكر
            var stopwatch = Stopwatch.StartNew();
            var matches = 0;
            foreach (var row in rows)
            {
                if (SearchMatcher.Match("تشطيت", row) is not null) matches++; // تشطيت بدل تشطيب
            }
            stopwatch.Stop();

            Assert.Equal(rows.Length, matches); // كل الصفوف فيها "تشطيب" فعلًا، فالفَزّي المفروض يلاقيها كلها
            Assert.True(stopwatch.ElapsedMilliseconds < 1000,
                $"البحث في {rows.Length} صف أخد {stopwatch.ElapsedMilliseconds}ms — أبطأ من المتوقع");
        }
    }
}
