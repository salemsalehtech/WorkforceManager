using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>أيقونة "؟" — شوف الكومنت في HelpIcon.xaml للتفاصيل الكاملة</summary>
    public partial class HelpIcon : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(HelpIcon), new PropertyMetadata(""));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        private bool _mouseOver;
        private bool _keyboardFocused;

        public HelpIcon()
        {
            InitializeComponent();

            HelpButton.MouseEnter += (_, _) => SetState(mouseOver: true);
            HelpButton.MouseLeave += (_, _) => SetState(mouseOver: false);
            HelpButton.GotKeyboardFocus += (_, _) => SetState(keyboardFocused: true);
            HelpButton.LostKeyboardFocus += (_, _) => SetState(keyboardFocused: false);
        }

        private void SetState(bool? mouseOver = null, bool? keyboardFocused = null)
        {
            if (mouseOver is { } m) _mouseOver = m;
            if (keyboardFocused is { } k) _keyboardFocused = k;
            HelpPopup.IsOpen = _mouseOver || _keyboardFocused;
        }
    }
}
