using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkforceManager.UI;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// السهم ↓/↑ فعليًا بينقل الفوكس للخانة اللي تحت/فوق — بنافذة حقيقية ظاهرة وضغطة مفتاح حقيقية عبر
    /// InputManager (نفس مسار الكيبورد)، وفيه زراير بين الخانات زي صفوف "الإنتاج اليومي للمنتجات".
    /// </summary>
    [Collection("WPF")]
    public class ArrowNavigationBehaviorTests
    {
        private static bool _registered;

        private static void EnsureHandler()
        {
            if (_registered) return;
            EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewKeyDownEvent,
                new KeyEventHandler(KeyboardShortcuts.ArrowNavigation));
            _registered = true;
        }

        private static (Window Window, TextBox[] Boxes) BuildRows()
        {
            var panel = new StackPanel();
            var boxes = new TextBox[3];
            for (var i = 0; i < 3; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
                var button = new Button { Content = "استخدم نفس الرقم", Width = 140 };
                DockPanel.SetDock(button, Dock.Left);
                row.Children.Add(button);
                boxes[i] = new TextBox { Text = (i * 100).ToString(), Width = 100 };
                row.Children.Add(boxes[i]);
                panel.Children.Add(row);
            }

            var window = new Window { Content = panel, Width = 500, Height = 400, ShowActivated = true, FlowDirection = FlowDirection.RightToLeft };
            window.Show();
            window.UpdateLayout();
            return (window, boxes);
        }

        private static void Press(UIElement target, Key key)
        {
            var source = PresentationSource.FromVisual(target)!;
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            InputManager.Current.ProcessInput(args);
        }

        [Fact]
        public void Down_moves_to_the_box_below_skipping_buttons_and_up_comes_back()
        {
            var (below, back) = WpfThread.Run(() =>
            {
                EnsureHandler();
                var (window, boxes) = BuildRows();
                try
                {
                    boxes[0].Focus();
                    Press(boxes[0], Key.Down);
                    var afterDown = ReferenceEquals(Keyboard.FocusedElement, boxes[1]);

                    Press(boxes[1], Key.Up);
                    var afterUp = ReferenceEquals(Keyboard.FocusedElement, boxes[0]);
                    return (afterDown, afterUp);
                }
                finally
                {
                    window.Close();
                }
            });

            Assert.True(below);
            Assert.True(back);
        }
    }
}
