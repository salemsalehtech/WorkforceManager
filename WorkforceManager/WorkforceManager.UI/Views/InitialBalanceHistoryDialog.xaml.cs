using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    public partial class InitialBalanceHistoryDialog : Window
    {
        public InitialBalanceHistoryDialog(string balanceName, int quantity, int usedQuantity, IReadOnlyList<InitialBalanceUsageDto> history)
        {
            InitializeComponent();

            TitleText.Text = $"الرصيد: {balanceName}";
            SummaryText.Text = $"إجمالي {quantity:N0} قطعة — مستخدم {usedQuantity:N0} قطعة";

            var rows = history.Select(item => new InitialBalanceHistoryRow(
                item.UsedDate.ToString("yyyy/MM/dd"),
                $"{item.Quantity:N0} قطعة",
                $"{item.WorkerName} · {item.StageName}",
                string.IsNullOrWhiteSpace(item.Notes)
                    ? "مفيش ملاحظات"
                    : item.Notes!,
                string.IsNullOrWhiteSpace(item.RecordedBy)
                    ? $"تسجيل {item.CreatedAt:yyyy/MM/dd HH:mm}"
                    : $"{item.RecordedBy} · {item.CreatedAt:yyyy/MM/dd HH:mm}")).ToList();

            HistoryList.ItemsSource = rows;
            NoHistoryText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }

    public record InitialBalanceHistoryRow(string DateText, string QuantityText, string Headline, string Notes, string Details);
}
