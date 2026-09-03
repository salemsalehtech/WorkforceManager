using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بيسأل بس "كام قطعة" لسحب نطاق واحد من رصيد أولي — تاريخ السحب هو
    /// تاريخ شاشة الإنتاج اليومي نفسها (مفيش تاريخ منفصل هنا)، واختيار
    /// العامل والمرحلة بيتم من كروت المراحل العادية بعد التأكيد (شوف
    /// FlowSessionViewModel.QueueWithdrawal).
    /// </summary>
    public partial class InitialBalanceUsageDialog : Window
    {
        public InitialBalanceUsageDialog(string balanceName, string rangeDescription, int remainingQuantity)
        {
            InitializeComponent();
            Loaded += (_, _) => QuantityBox.Focus();

            BalanceText.Text = $"الرصيد: {balanceName}";
            RangeText.Text = rangeDescription;
            RemainingText.Text = $"المتاح للسحب: {remainingQuantity:N0} قطعة";

            QuantityBox.Text = remainingQuantity.ToString();
        }

        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

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
