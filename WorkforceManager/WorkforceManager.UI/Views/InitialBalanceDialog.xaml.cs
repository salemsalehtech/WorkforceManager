using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    public partial class InitialBalanceDialog : Window
    {
        public InitialBalanceDialog()
        {
            InitializeComponent();
            RangesListControl.ItemsSource = Ranges;
            Loaded += (_, _) => NameBox.Focus();
            OriginalDatePicker.SelectedDate = DateTime.Today;
        }

        public ObservableCollection<DialogRangeItem> Ranges { get; } = new();
        private IReadOnlyList<BalanceStageChoice> _stages = Array.Empty<BalanceStageChoice>();

        public void LoadStages(IEnumerable<StageEntryOption> stages)
        {
            _stages = stages.Select(s => new BalanceStageChoice(s.StageId, s.Display)).ToList();
        }

        private void AddRange_Click(object sender, RoutedEventArgs e)
        {
            var range = new DialogRangeItem(_stages);
            if (_stages.Count > 0) range.FromStage = _stages[0];
            if (_stages.Count > 1) range.ToStage = _stages[1];
            else if (_stages.Count > 0) range.ToStage = _stages[0];
            Ranges.Add(range);
        }

        private void RemoveRange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DialogRangeItem item })
            {
                Ranges.Remove(item);
            }
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
            RangesSection.Visibility = Visibility.Collapsed;
        }

        public string BalanceName => NameBox.Text.Trim();
        public string Reason => ReasonBox.Text.Trim();
        public string? Notes => string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        public int Quantity => int.TryParse(QuantityBox.Text.Trim(), out var quantity) ? quantity : 0;
        public DateTime OriginalDate => OriginalDatePicker.SelectedDate ?? DateTime.Today;

        public List<AddInitialBalanceRangeRequest> GetRanges()
        {
            return Ranges.Select(r => new AddInitialBalanceRangeRequest
            {
                FromStageId = r.FromStage?.StageId ?? 0,
                ToStageId = r.ToStage?.StageId ?? 0,
                PieceCount = int.TryParse(r.QuantityText.Trim(), out var q) ? q : 0
            }).ToList();
        }

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

            foreach (var range in Ranges)
            {
                if (range.FromStage is null)
                {
                    ErrorText.ShowError("اختر مرحلة البداية في جميع النطاقات");
                    return;
                }
                if (range.ToStage is null)
                {
                    ErrorText.ShowError("اختر مرحلة النهاية في جميع النطاقات");
                    return;
                }
                if (!int.TryParse(range.QuantityText.Trim(), out var rq) || rq <= 0)
                {
                    ErrorText.ShowError("عدد قطع النطاق يجب أن يكون رقمًا أكبر من صفر");
                    return;
                }
            }

            DialogResult = true;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }

    public class DialogRangeItem
    {
        public DialogRangeItem(IReadOnlyList<BalanceStageChoice> stages)
        {
            Stages = stages;
        }
        
        public IReadOnlyList<BalanceStageChoice> Stages { get; }
        public BalanceStageChoice? FromStage { get; set; }
        public BalanceStageChoice? ToStage { get; set; }
        public string QuantityText { get; set; } = "";
    }
}
