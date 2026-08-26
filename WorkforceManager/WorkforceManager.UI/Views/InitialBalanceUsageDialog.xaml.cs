using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    public partial class InitialBalanceUsageDialog : Window
    {
        public InitialBalanceUsageDialog(
            IReadOnlyList<WorkerChoice> workers,
            IReadOnlyList<StageChoice> stages,
            IReadOnlyList<InitialBalanceRangeDto> ranges,
            string balanceName,
            int remainingQuantity)
        {
            InitializeComponent();
            Loaded += (_, _) => QuantityBox.Focus();
            UsedDatePicker.SelectedDate = DateTime.Today;

            BalanceText.Text = $"الرصيد: {balanceName}";
            RemainingText.Text = $"المتبقي الآن: {remainingQuantity} قطعة";

            WorkerBox.ItemsSource = workers;
            StageBox.ItemsSource = stages;

            var rangeChoices = new List<RangeChoice>
            {
                new(null, "بدون نطاق")
            };
            rangeChoices.AddRange(ranges.Select(r => new RangeChoice(r.Id, $"{r.FromStageName} → {r.ToStageName} ({r.PieceCount} قطعة)")));
            RangeBox.ItemsSource = rangeChoices;
            RangeBox.SelectedIndex = 0;

            if (workers.Count > 0) WorkerBox.SelectedIndex = 0;
            if (stages.Count > 0) StageBox.SelectedIndex = 0;
            QuantityBox.Text = Math.Min(remainingQuantity, 1).ToString();
        }

        public int? WorkerId => (WorkerBox.SelectedItem as WorkerChoice)?.WorkerId;
        public int? StageId => (StageBox.SelectedItem as StageChoice)?.StageId;
        public int? RangeId => (RangeBox.SelectedItem as RangeChoice)?.RangeId;
        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;
        public DateTime UsedDate => UsedDatePicker.SelectedDate ?? DateTime.Today;
        public string? Notes => string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

            if (WorkerId is null)
            {
                ErrorText.ShowError("اختر العامل أولًا");
                WorkerBox.Focus();
                return;
            }

            if (StageId is null)
            {
                ErrorText.ShowError("اختر المرحلة أولًا");
                StageBox.Focus();
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

    public record WorkerChoice(int WorkerId, string Name);
    public record StageChoice(int StageId, string Name);
    public record RangeChoice(int? RangeId, string Name);
}
