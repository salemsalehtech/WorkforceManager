using WorkforceManager.UI.Tour;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>مطابقة الأوامر المباشرة في البحث السريع (منطق نقي، مفيش WPF)</summary>
    public class SearchableActionsTests
    {
        [Fact]
        public void Record_absence_with_a_name_matches_the_attendance_action_and_keeps_the_name()
        {
            var match = SearchableActions.Match("سجّل غياب أحمد").Single(m => m.Exact);

            Assert.Equal(SearchableActions.RecordAttendance, match.Action.Key);
            Assert.Equal("احمد", match.Remainder);
        }

        [Fact]
        public void Add_worker_has_no_remainder()
        {
            var match = SearchableActions.Match("أضف عامل").Single(m => m.Exact);

            Assert.Equal(SearchableActions.AddWorker, match.Action.Key);
            Assert.Equal("", match.Remainder);
        }

        [Fact]
        public void A_phrase_still_being_typed_shows_the_action_as_a_partial_match()
        {
            var matches = SearchableActions.Match("سجل غ").ToList();

            Assert.Contains(matches, m => !m.Exact && m.Action.Key == SearchableActions.RecordAttendance);
        }

        [Fact]
        public void Plain_worker_name_is_not_an_action()
        {
            Assert.DoesNotContain(SearchableActions.Match("أحمد عبدالله"), m => m.Exact);
        }

        [Fact]
        public void Final_save_phrase_matches()
        {
            Assert.Contains(SearchableActions.Match("حفظ نهائي"), m => m.Exact && m.Action.Key == SearchableActions.FinalSave);
        }
    }
}
