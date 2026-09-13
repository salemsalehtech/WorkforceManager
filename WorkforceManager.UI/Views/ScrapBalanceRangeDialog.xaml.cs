using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Core.Models;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// تأكيد صغير لتحويل جزء (أو كل) نطاق رصيد أولي لهالك — بعكس
    /// <see cref="ScrapDialog"/> العام، المنتج/المرحلة هنا محددين بالفعل
    /// من النطاق المختار، فالحوار بيسأل بس عن الكمية/سبب/ملاحظة/تاريخ.
    /// </summary>
    public partial class ScrapBalanceRangeDialog : Window
    {
        private readonly int _remainingQuantity;

        public ScrapBalanceRangeDialog(
            string balanceName, string rangeDescription, int remainingQuantity,
            IReadOnlyList<ScrapReason> reasons, DateTime defaultDate)
        {
            InitializeComponent();
            Loaded += (_, _) => QuantityBox.Focus();

            _remainingQuantity = remainingQuantity;

            BalanceText.Text = $"الرصيد: {balanceName}";
            RangeText.Text = rangeDescription;
            RemainingText.Text = $"المتاح للتحويل: {remainingQuantity:N0} قطعة";
            QuantityBox.Text = remainingQuantity.ToString();

            DateBox.SelectedDate = defaultDate;

            ReasonBox.ItemsSource = reasons;
            if (reasons.Count > 0) ReasonBox.SelectedIndex = 0;
        }

        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;
        public DateTime Date => DateBox.SelectedDate ?? DateTime.Today;
        public int? ReasonId => (ReasonBox.SelectedItem as ScrapReason)?.Id;
        public string? Note => string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

            if (Quantity <= 0)
            {
                ErrorText.ShowError("عدد القطع يجب أن يكون أكبر من صفر");
                QuantityBox.Focus();
                return;
            }

            if (Quantity > _remainingQuantity)
            {
                ErrorText.ShowError($"عدد القطع أكبر من المتاح في النطاق ({_remainingQuantity:N0})");
                QuantityBox.Focus();
                return;
            }

            if (DateBox.SelectedDate is null)
            {
                ErrorText.ShowError("اختار تاريخ التحويل");
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
