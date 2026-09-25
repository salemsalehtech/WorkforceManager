using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WorkforceManager.UI;
using Xunit;

namespace WorkforceManager.UiTests
{
    /// <summary>اختصارات الكيبورد العامة — شوف KeyboardShortcuts وخريطة CLAUDE.md</summary>
    public class KeyboardShortcutsTests
    {
        [Theory]
        [InlineData(Key.N, ModifierKeys.Control, ShortcutAction.QuickAdd)]
        [InlineData(Key.S, ModifierKeys.Control, ShortcutAction.Save)]
        [InlineData(Key.F, ModifierKeys.Control, ShortcutAction.Search)]
        // كتابة الحرف عادي (من غير Ctrl) عمرها ما تبقى اختصار — اسم فيه "N" مثلًا
        [InlineData(Key.N, ModifierKeys.None, ShortcutAction.None)]
        [InlineData(Key.S, ModifierKeys.Shift, ShortcutAction.None)]
        [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.None)]
        // Ctrl+K وCtrl+B ليهم معالجهم الخاص في MainWindow من قبل كده
        [InlineData(Key.K, ModifierKeys.Control, ShortcutAction.None)]
        [InlineData(Key.B, ModifierKeys.Control, ShortcutAction.None)]
        public void Resolve_MapsOnlyCtrlGestures(Key key, ModifierKeys modifiers, ShortcutAction expected)
        {
            Assert.Equal(expected, KeyboardShortcuts.Resolve(key, modifiers));
        }

        [Fact]
        public void TryExecute_RunsCommandWhenAllowed()
        {
            var ran = false;
            var command = new RelayCommand(() => ran = true);

            Assert.True(KeyboardShortcuts.TryExecute(command));
            Assert.True(ran);
        }

        [Fact]
        public void TryExecute_RespectsCanExecute()
        {
            // زي Ctrl+S وفورم مش صالح: الأمر مقفول، فالاختصار مايعملش حاجة
            var ran = false;
            var command = new RelayCommand(() => ran = true, () => false);

            Assert.False(KeyboardShortcuts.TryExecute(command));
            Assert.False(ran);
        }

        [Fact]
        public void TryExecute_NullCommand_DoesNothing()
        {
            Assert.False(KeyboardShortcuts.TryExecute(null));
        }

        [Fact]
        public void ShouldAdvanceOnEnter_OnlyPlainInputFields()
        {
            var (singleLine, multiLine, closedCombo, button) = WpfThread.Run(() => (
                KeyboardShortcuts.ShouldAdvanceOnEnter(new TextBox()),
                KeyboardShortcuts.ShouldAdvanceOnEnter(new TextBox { AcceptsReturn = true }),
                KeyboardShortcuts.ShouldAdvanceOnEnter(new ComboBox()),
                KeyboardShortcuts.ShouldAdvanceOnEnter(new Button())));

            Assert.True(singleLine);
            Assert.False(multiLine);
            Assert.True(closedCombo);
            Assert.False(button);
        }

        [Fact]
        public void CtrlS_InDialog_ClicksEnabledDefaultButton()
        {
            var saved = WpfThread.Run(() => ShowDialogAndPressSave(saveEnabled: true));
            Assert.True(saved);
        }

        [Fact]
        public void CtrlS_InDialog_IgnoresDisabledDefaultButton()
        {
            var saved = WpfThread.Run(() => ShowDialogAndPressSave(saveEnabled: false));
            Assert.False(saved);
        }

        /// <summary>ديالوج بزرار حفظ افتراضي — Ctrl+S (TryClickDefaultButton) بعد ما يتعرض، والنتيجة DialogResult</summary>
        private static bool ShowDialogAndPressSave(bool saveEnabled)
        {
            var save = new Button { Content = "حفظ", IsDefault = true, IsEnabled = saveEnabled };
            var window = new Window
            {
                Content = new StackPanel { Children = { new TextBox(), save } },
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            save.Click += (_, _) => window.DialogResult = true;

            window.Loaded += (_, _) =>
            {
                var clicked = KeyboardShortcuts.TryClickDefaultButton(window);
                // مفيش زرار اتداس → نقفل بإيدنا عشان ShowDialog يرجع
                if (!clicked) window.Dispatcher.BeginInvoke(() => window.Close());
            };

            return window.ShowDialog() == true;
        }
    }
}
