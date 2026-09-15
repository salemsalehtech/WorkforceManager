using WorkforceManager.Core.Helpers;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// صيغة ترقية "الترتيب بالاستخدام" بمعزل تام — منطق نقي، معامل "دلوقتي"
    /// صريح بدل الاعتماد على DateTime.Now الحقيقي وقت الاختبار (نفس مبدأ
    /// SearchMatcherTests للتطابق).
    /// </summary>
    public class SearchRankingScorerTests
    {
        private static DateTime Now => new(2026, 6, 15);

        [Fact]
        public void No_picks_means_no_boost()
        {
            Assert.Equal(0, SearchRankingScorer.ComputeBoost(0, Now, Now));
        }

        [Fact]
        public void More_repeated_picks_at_the_same_recency_give_a_higher_boost()
        {
            var onePick = SearchRankingScorer.ComputeBoost(1, Now, Now);
            var threePicks = SearchRankingScorer.ComputeBoost(3, Now, Now);

            Assert.True(threePicks > onePick);
        }

        [Fact]
        public void Boost_never_exceeds_the_hard_cap_even_with_many_picks()
        {
            var boost = SearchRankingScorer.ComputeBoost(1000, Now, Now);

            Assert.True(boost <= SearchRankingScorer.MaxRankingBoost);
        }

        [Fact]
        public void The_hard_cap_stays_well_below_a_full_classification_tier_gap()
        {
            // فرق درجة تصنيف كامل في SearchMatcher = 200 (Exact 1000 لـ Prefix 800) —
            // الترقية لازم تفضل أقل منه، وإلا نتيجة Fuzzy ضعيفة ممكن تقفز فوق تطابق دقيق
            Assert.True(SearchRankingScorer.MaxRankingBoost < 200);
        }

        [Fact]
        public void An_older_pick_gives_a_smaller_boost_than_a_more_recent_one_with_the_same_count()
        {
            var recent = SearchRankingScorer.ComputeBoost(2, Now, Now);
            var old = SearchRankingScorer.ComputeBoost(2, Now.AddDays(-60), Now);

            Assert.True(old < recent);
        }

        [Fact]
        public void One_old_pick_does_not_outweigh_several_more_recent_different_picks()
        {
            // اختيار واحد اتكرر كتير قبل كده لكن بقاله زمن (180 يوم = 6 أضعاف
            // نصف العمر) لازم يدوب لأقل بكتير من اختيار حديث واحد بس
            var oldRepeated = SearchRankingScorer.ComputeBoost(5, Now.AddDays(-180), Now);
            var freshSingle = SearchRankingScorer.ComputeBoost(1, Now, Now);

            Assert.True(oldRepeated < freshSingle);
        }

        [Fact]
        public void Boost_roughly_halves_after_one_recency_half_life()
        {
            var fresh = SearchRankingScorer.ComputeBoost(4, Now, Now);
            var afterHalfLife = SearchRankingScorer.ComputeBoost(4, Now.AddDays(-SearchRankingScorer.RecencyHalfLifeDays), Now);

            // تقريبي (تقريب Math.Round) بدل مساواة عشرية دقيقة
            Assert.InRange(afterHalfLife, fresh / 2 - 2, fresh / 2 + 2);
        }
    }

    /// <summary>
    /// التخزين الفعلي (ملف JSON معزول لكل اختبار) — نفس نمط ReportTemplateTests
    /// بالظبط: مسار مؤقت فريد، بيتشال بعد كل اختبار.
    /// </summary>
    public class SearchRankingStoreTests : IDisposable
    {
        private readonly string _path =
            Path.Combine(Path.GetTempPath(), $"wm-search-ranking-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ملف مؤقت */ }
        }

        [Fact]
        public void Missing_file_returns_empty_data_not_an_exception()
        {
            var data = SearchRankingStore.Load(_path);

            Assert.Empty(data.PicksByQuery);
        }

        [Fact]
        public void Corrupt_file_returns_empty_data_instead_of_crashing()
        {
            File.WriteAllText(_path, "{ not valid json at all");

            var data = SearchRankingStore.Load(_path);

            Assert.Empty(data.PicksByQuery);
        }

        [Fact]
        public void First_pick_for_a_query_is_recorded_with_count_one()
        {
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 1), _path);

            var data = SearchRankingStore.Load(_path);

            var pick = Assert.Single(data.PicksByQuery["غياب احمد"]);
            Assert.Equal("Worker:1", pick.ResultKey);
            Assert.Equal(1, pick.PickCount);
        }

        [Fact]
        public void Repeating_the_same_pick_for_the_same_query_increments_the_count()
        {
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 1), _path);
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 5), _path);
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 10), _path);

            var data = SearchRankingStore.Load(_path);

            var pick = Assert.Single(data.PicksByQuery["غياب احمد"]);
            Assert.Equal(3, pick.PickCount);
            Assert.Equal(new DateTime(2026, 6, 10), pick.LastPickedAt);
        }

        [Fact]
        public void Different_results_picked_for_the_same_query_are_tracked_separately()
        {
            SearchRankingStore.RecordPick("دبله", "Product:1", new DateTime(2026, 6, 1), _path);
            SearchRankingStore.RecordPick("دبله", "Product:2", new DateTime(2026, 6, 2), _path);

            var data = SearchRankingStore.Load(_path);

            Assert.Equal(2, data.PicksByQuery["دبله"].Count);
        }

        [Fact]
        public void Recent_queries_are_most_recently_picked_first()
        {
            SearchRankingStore.RecordPick("دبله", "Product:1", new DateTime(2026, 6, 1), _path);
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 3), _path);
            SearchRankingStore.RecordPick("راتب سعيد", "Worker:2", new DateTime(2026, 6, 2), _path);

            var recent = SearchRankingStore.GetRecentQueries(2, _path);

            Assert.Equal(new[] { "غياب احمد", "راتب سعيد" }, recent);
        }

        [Fact]
        public void Recent_queries_uses_the_latest_pick_within_a_query_that_has_several()
        {
            // "دبله" اتختار لها Product:1 بدري، وبعدين Product:2 متأخر أكتر —
            // آخر اختيار فعلي للاستعلام ده هو 6/10، مش 6/1
            SearchRankingStore.RecordPick("دبله", "Product:1", new DateTime(2026, 6, 1), _path);
            SearchRankingStore.RecordPick("دبله", "Product:2", new DateTime(2026, 6, 10), _path);
            SearchRankingStore.RecordPick("غياب احمد", "Worker:1", new DateTime(2026, 6, 5), _path);

            var recent = SearchRankingStore.GetRecentQueries(2, _path);

            Assert.Equal(new[] { "دبله", "غياب احمد" }, recent);
        }

        [Fact]
        public void Recent_queries_on_an_empty_store_returns_an_empty_list()
        {
            Assert.Empty(SearchRankingStore.GetRecentQueries(4, _path));
        }
    }
}
