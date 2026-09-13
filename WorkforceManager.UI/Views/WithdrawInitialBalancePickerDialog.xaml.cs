using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// بيختار رصيد أولي + نطاق (أو "كل النطاقات") + كمية، عشان زرار
    /// "أخذ من الرصيد الأولي" اللي جوه قسم توزيع الإنتاج. بعد التأكيد
    /// النطاقات بتتحط في الرحلة والعامل بيتوزّع من كروت المراحل زي أي
    /// نطاق عادي (شوف FlowSessionViewModel.QueueWithdrawal). المتاح
    /// المعروض هنا تقريبي — WithdrawAsync بيتحقق من الحقيقي وقت الحفظ.
    /// </summary>
    public partial class WithdrawInitialBalancePickerDialog : Window
    {
        private sealed record BalanceChoice(InitialBalanceDto Balance, string Display);
        private sealed record RangeChoice(InitialBalanceRangeDto? Range, string Display, int DefaultQuantity);

        public WithdrawInitialBalancePickerDialog(IReadOnlyList<InitialBalanceDto> balances)
        {
            InitializeComponent();
            Loaded += (_, _) => BalanceBox.Focus();

            BalanceBox.ItemsSource = balances
                .Select(b => new BalanceChoice(b, $"{b.Name} — متبقي {b.RemainingQuantity:N0}"))
                .ToList();

            if (BalanceBox.Items.Count > 0)
                BalanceBox.SelectedIndex = 0;
        }

        public InitialBalanceDto? SelectedBalance => (BalanceBox.SelectedItem as BalanceChoice)?.Balance;

        /// <summary>null معناها "كل النطاقات"</summary>
        public InitialBalanceRangeDto? SelectedRange => (RangeBox.SelectedItem as RangeChoice)?.Range;

        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;

        private void BalanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedBalance is not { } balance)
            {
                RangeBox.ItemsSource = null;
                return;
            }

            var choices = new List<RangeChoice>();
            if (balance.Ranges.Count > 1)
                choices.Add(new RangeChoice(null, "كل النطاقات", balance.Ranges.Sum(r => r.PieceCount)));

            choices.AddRange(balance.Ranges.Select(r => new RangeChoice(
                r, $"{r.FromStageName} ← {r.ToStageName} ({r.PieceCount:N0})", r.PieceCount)));

            RangeBox.ItemsSource = choices;
            RangeBox.SelectedIndex = choices.Count > 0 ? 0 : -1;
        }

        private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeBox.SelectedItem is not RangeChoice choice) return;
            QuantityBox.Text = choice.DefaultQuantity.ToString();
            RemainingText.Text = SelectedBalance is { } b ? $"متاح في الرصيد: {b.RemainingQuantity:N0} قطعة" : "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

            if (SelectedBalance is null)
            {
                ErrorText.ShowError("اختر الرصيد أولًا");
                return;
            }

            if (RangeBox.SelectedItem is not RangeChoice)
            {
                ErrorText.ShowError("الرصيد ده مفيهوش نطاقات تتسحب منها");
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
