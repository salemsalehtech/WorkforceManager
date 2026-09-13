using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    public partial class InitialBalanceHistoryDialog : Window
    {
        private readonly List<InitialBalanceHistoryRow> _operationRows;
        private readonly List<InitialBalanceHistoryRow> _workerRows;
        private bool _showingWorkers;

        public InitialBalanceHistoryDialog(string balanceName, int quantity, int usedQuantity, IReadOnlyList<InitialBalanceUsageDto> history)
        {
            InitializeComponent();

            TitleText.Text = $"الرصيد: {balanceName}";
            SummaryText.Text = $"إجمالي {quantity:N0} قطعة — مستخدم {usedQuantity:N0} قطعة";

            _operationRows = history.Select(item => new InitialBalanceHistoryRow(
                item.UsedDate.ToString("yyyy/MM/dd"),
                $"{item.Quantity:N0} قطعة",
                $"{item.WorkerName} · {item.StageName}",
                string.IsNullOrWhiteSpace(item.Notes)
                    ? "مفيش ملاحظات"
                    : item.Notes!,
                string.IsNullOrWhiteSpace(item.RecordedBy)
                    ? $"تسجيل {item.CreatedAt:yyyy/MM/dd HH:mm}"
                    : $"{item.RecordedBy} · {item.CreatedAt:yyyy/MM/dd HH:mm}")).ToList();

            // "عرض العمال": كل عامل اشتغل على أنهي مرحلة فعليًا من الرصيد ده —
            // مُجمّعة (عامل + مرحلة)، مش سطر لكل عملية سحب زي القايمة الأصلية.
            // سحب الهالك (WorkerName فاضي — مفيش عامل) مُستبعد هنا عن قصد،
            // لأن السؤال هنا "مين اشتغل" لا "كام قطعة راحت هالك"
            _workerRows = history
                .Where(item => !string.IsNullOrWhiteSpace(item.WorkerName))
                .GroupBy(item => (item.WorkerName, item.StageName))
                .OrderBy(g => g.Key.WorkerName).ThenBy(g => g.Key.StageName)
                .Select(g => new InitialBalanceHistoryRow(
                    $"{g.Count():N0} عملية",
                    $"{g.Sum(i => i.Quantity):N0} قطعة",
                    $"{g.Key.WorkerName} · {g.Key.StageName}",
                    string.Empty,
                    $"من {g.Min(i => i.UsedDate):yyyy/MM/dd} لـ {g.Max(i => i.UsedDate):yyyy/MM/dd}"))
                .ToList();

            ApplyView();
        }

        private void ToggleWorkersView_Click(object sender, RoutedEventArgs e)
        {
            _showingWorkers = !_showingWorkers;
            ApplyView();
        }

        private void ApplyView()
        {
            var rows = _showingWorkers ? _workerRows : _operationRows;
            ToggleWorkersButtonText.Text = _showingWorkers ? "رجوع لسجل العمليات" : "عرض العمال";

            HistoryList.ItemsSource = rows;
            NoHistoryText.Text = _showingWorkers
                ? "مفيش عمال سجّلوا إنتاج على الرصيد ده لسه"
                : "لا يوجد استخدامات مسجلة لهذا الرصيد بعد";
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
