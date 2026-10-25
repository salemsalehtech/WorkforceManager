using System.Windows.Controls;
using WorkforceManager.UI;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>مين بيتنقل بالأسهم ↑/↓ ومين لأ (KeyboardShortcuts.ShouldMoveWithArrows)</summary>
    [Collection("WPF")]
    public class ArrowNavigationTests
    {
        [Fact]
        public void Plain_single_line_box_moves_with_arrows() =>
            Assert.True(WpfThread.Run(() => KeyboardShortcuts.ShouldMoveWithArrows(new TextBox())));

        [Fact]
        public void Multi_line_box_keeps_arrows_for_its_own_lines() =>
            Assert.False(WpfThread.Run(() => KeyboardShortcuts.ShouldMoveWithArrows(new TextBox { AcceptsReturn = true })));

        [Fact]
        public void Search_boxes_keep_arrows_for_their_suggestions() =>
            Assert.False(WpfThread.Run(() => KeyboardShortcuts.ShouldMoveWithArrows(new TextBox { Name = "WorkerSearchBox" })));

        [Fact]
        public void Box_inside_a_list_keeps_arrows_for_the_list() =>
            Assert.False(WpfThread.Run(() =>
            {
                var box = new TextBox();
                var list = new ListBox();
                list.Items.Add(box);
                return KeyboardShortcuts.ShouldMoveWithArrows(box);
            }));
    }
}
