using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    public partial class InitialBalanceDialog : Window
    {
        public InitialBalanceDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameBox.Focus();
            OriginalDatePicker.SelectedDate = DateTime.Today;
        }

        public void LoadBalance(string name, string reason, string? notes, int quantity, DateTime originalDate)
        {
            HeaderText.Text = "تعديل رصيد أولي";
            NameBox.Text = name;
            ReasonBox.Text = reason;
            NotesBox.Text = notes ?? string.Empty;
            QuantityBox.Text = quantity.ToString();
            QuantityBox.IsEnabled = false;
            OriginalDatePicker.SelectedDate = originalDate;
            OriginalDatePicker.IsEnabled = false;
        }

        public string BalanceName => NameBox.Text.Trim();
        public string Reason => ReasonBox.Text.Trim();
        public string? Notes => string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;
        public DateTime OriginalDate => OriginalDatePicker.SelectedDate ?? DateTime.Today;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BalanceName))
            {
                ErrorText.ShowError("اسم الرصيد مطلوب");
                NameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(Reason))
            {
                ErrorText.ShowError("سبب الرصيد مطلوب");
                ReasonBox.Focus();
                return;
            }

            if (Quantity <= 0)
            {
                ErrorText.ShowError("عدد القطع يجب أن يكون أكبر من صفر");
                QuantityBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
