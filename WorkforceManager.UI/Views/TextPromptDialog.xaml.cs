using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// ديالوج عام لسطر نص واحد — شوف تعليق XAML. <see cref="Validate"/>
    /// اختيارية: بترجع رسالة خطأ لو القيمة مش مقبولة (مثلاً اسم مكرر)،
    /// null لو تمام.
    /// </summary>
    public partial class TextPromptDialog : Window
    {
        private readonly Func<string, string?>? _validate;

        public TextPromptDialog(
            string title, string fieldLabel, string initialValue = "", Func<string, string?>? validate = null)
        {
            InitializeComponent();
            _validate = validate;

            Title = title;
            HeaderText.Text = title;
            FieldLabelText.Text = fieldLabel;
            ValueBox.Text = initialValue;

            Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
        }

        public string Value => ValueBox.Text.Trim();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ValueBox.Text))
            {
                ShowError("الحقل ده مطلوب");
                return;
            }

            var error = _validate?.Invoke(Value);
            if (error is not null)
            {
                ShowError(error);
                return;
            }

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
