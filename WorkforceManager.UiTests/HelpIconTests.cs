using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WorkforceManager.UI.Tour;
using WorkforceManager.UI.Views;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>
    /// أيقونة "؟" — لازم تفتح البابل بالهوفر وبالتنقل بالكيبورد الاتنين
    /// (مش ToolTip العادي اللي بيشتغل بالهوفر بس)، وتقفل لما الاتنين يخلصوا
    /// </summary>
    public class HelpIconTests
    {
        [Fact]
        public void Popup_OpensOnMouseEnter_ClosesOnMouseLeave()
        {
            var (afterEnter, afterLeave) = WpfThread.Run(() =>
            {
                var icon = BuildRealizedIcon();
                Raise(icon, "MouseEnter");
                var opened = Popup(icon).IsOpen;
                Raise(icon, "MouseLeave");
                return (opened, Popup(icon).IsOpen);
            });

            Assert.True(afterEnter);
            Assert.False(afterLeave);
        }

        [Fact]
        public void Popup_OpensOnKeyboardFocus_ClosesOnFocusLost()
        {
            var (afterFocus, afterLostFocus) = WpfThread.Run(() =>
            {
                var icon = BuildRealizedIcon();
                Raise(icon, "GotKeyboardFocus");
                var opened = Popup(icon).IsOpen;
                Raise(icon, "LostKeyboardFocus");
                return (opened, Popup(icon).IsOpen);
            });

            Assert.True(afterFocus);
            Assert.False(afterLostFocus);
        }

        [Fact]
        public void Popup_StaysOpen_WhileEitherConditionHolds()
        {
            // ماوس فوقه وبعدين فوكس عليه (كيبورد) — لسه لازم يفضل مفتوح
            // لحد ما الاتنين يخلصوا، مش أول واحد بس
            var stillOpenAfterMouseLeaves = WpfThread.Run(() =>
            {
                var icon = BuildRealizedIcon();
                Raise(icon, "MouseEnter");
                Raise(icon, "GotKeyboardFocus");
                Raise(icon, "MouseLeave");
                return Popup(icon).IsOpen;
            });

            Assert.True(stillOpenAfterMouseLeaves);
        }

        [Theory]
        [InlineData(nameof(FieldHelpText.PieceWeight))]
        [InlineData(nameof(FieldHelpText.Material))]
        [InlineData(nameof(FieldHelpText.AchievedPercent))]
        [InlineData(nameof(FieldHelpText.RequiredDailyOutput))]
        [InlineData(nameof(FieldHelpText.PaceStatus))]
        [InlineData(nameof(FieldHelpText.InitialBalance))]
        public void FieldHelpText_IsNonEmptyArabicSentence(string fieldName)
        {
            var text = (string)typeof(FieldHelpText).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.EndsWith(".", text); // مفيش نص فاضي أو ناقص يتشحن بالغلط
        }

        private static HelpIcon BuildRealizedIcon()
        {
            // Popup.IsOpen بيرفض يفتح لو PlacementTarget مش Visible —
            // نافذة Minimized IsVisible=false، فبنعرضها فعلًا برّه الشاشة بدل ما نصغّرها
            var icon = new HelpIcon { Text = "شرح تجريبي." };
            var window = new Window
            {
                Content = icon, ShowInTaskbar = false,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
                Left = -5000, Top = -5000, Width = 50, Height = 50
            };
            window.Show();
            return icon;
        }

        private static Button Button(HelpIcon icon) => (Button)icon.FindName("HelpButton")!;
        private static Popup Popup(HelpIcon icon) => (Popup)icon.FindName("HelpPopup")!;

        /// <summary>بيطلق حدث Mouse/KeyboardFocus مباشرة على HelpButton — الاختبار مش محتاج ماوس حقيقي أو نافذة تانية تاخد الفوكس</summary>
        private static void Raise(HelpIcon icon, string eventName)
        {
            var button = Button(icon);
            switch (eventName)
            {
                case "MouseEnter":
                    button.RaiseEvent(new MouseEventArgs(InputManager.Current.PrimaryMouseDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                    break;
                case "MouseLeave":
                    button.RaiseEvent(new MouseEventArgs(InputManager.Current.PrimaryMouseDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                    break;
                case "GotKeyboardFocus":
                    button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, button) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
                    break;
                case "LostKeyboardFocus":
                    button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, button, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                    break;
            }
        }
    }
}
