using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    public partial class AddInitialBalanceRangeDialog : Window
    {
        public AddInitialBalanceRangeDialog(
            IReadOnlyList<BalanceStageChoice> stages,
            string balanceName,
            int availableQuantity)
        {
            InitializeComponent();
            Loaded += (_, _) => QuantityBox.Focus();

            InfoText.Text = $"الرصيد: {balanceName}";
            AvailableText.Text = $"المتاح للتوزيع في النطاقات: {availableQuantity} قطعة";

            FromStageBox.ItemsSource = stages;
            ToStageBox.ItemsSource = stages;
            if (stages.Count > 0) FromStageBox.SelectedIndex = 0;
            if (stages.Count > 1) ToStageBox.SelectedIndex = 1;
            else if (stages.Count > 0) ToStageBox.SelectedIndex = 0;

            QuantityBox.Text = Math.Min(availableQuantity, 1).ToString();
        }

        public int? FromStageId => (FromStageBox.SelectedItem as BalanceStageChoice)?.StageId;
        public int? ToStageId => (ToStageBox.SelectedItem as BalanceStageChoice)?.StageId;
        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

            if (FromStageId is null)
            {
                ErrorText.ShowError("اختر مرحلة البداية");
                FromStageBox.Focus();
                return;
            }

            if (ToStageId is null)
            {
                ErrorText.ShowError("اختر مرحلة النهاية");
                ToStageBox.Focus();
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

    public record BalanceStageChoice(int StageId, string Name);
}
