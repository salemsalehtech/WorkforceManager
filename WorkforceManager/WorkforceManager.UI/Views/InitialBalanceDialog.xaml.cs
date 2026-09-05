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
                // زر الحذف متعطّل أصلاً في الـ XAML على نطاق عليه استخدام
                // (IsLocked) — الفحص هنا دفاعي بس، مش أول خط دفاع
                if (item.IsLocked) return;
                Ranges.Remove(item);
            }
        }

        /// <summary>
        /// وضع التعديل: الاسم/الملاحظات والكمية والتاريخ الأصلي زي ما هو
        /// (الكمية/التاريخ مقفولين، شوف تعليقات EditAsync). النطاقات بتتحمل
        /// من الرصيد الحالي — أي نطاق عليه استخدام (<see cref="InitialBalanceRangeDto.UsedQuantity"/> > 0)
        /// امتداده (من/لمرحلة) وزرار حذفه بيتقفلوا، وعدد قطعه بس قابل
        /// للتعديل لحد أرضية المستخدم.
        /// </summary>
        public void LoadBalance(InitialBalanceDto balance)
        {
            HeaderText.Text = "تعديل رصيد أولي";
            NameBox.Text = balance.Name;
            NotesBox.Text = balance.Notes ?? string.Empty;
            QuantityBox.Text = balance.Quantity.ToString();
            QuantityBox.IsEnabled = false;
            OriginalDatePicker.SelectedDate = balance.OriginalDate;
            OriginalDatePicker.IsEnabled = false;

            Ranges.Clear();
            foreach (var r in balance.Ranges)
            {
                Ranges.Add(new DialogRangeItem(_stages)
                {
                    Id = r.Id,
                    FromStage = _stages.FirstOrDefault(s => s.StageId == r.FromStageId),
                    ToStage = _stages.FirstOrDefault(s => s.StageId == r.ToStageId),
                    QuantityText = r.PieceCount.ToString(),
                    UsedQuantity = r.UsedQuantity
                });
            }
        }

        public string BalanceName => NameBox.Text.Trim();
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

        /// <summary>
        /// نفس قايمة النطاقات لكن بشكل EditAsync بيفهمه — نطاق قديم بيحمل
        /// Id (امتداده بيتجاهل في الـ Business layer، القيم هنا للعرض بس)،
        /// نطاق جديد Id يبقى null.
        /// </summary>
        public List<InitialBalanceRangeEditItem> GetRangeEdits()
        {
            return Ranges.Select(r => new InitialBalanceRangeEditItem
            {
                Id = r.Id,
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
                if (rq < range.UsedQuantity)
                {
                    ErrorText.ShowError($"عدد قطع النطاق مايقلّش عن الكمية المستخدمة منه ({range.UsedQuantity:N0})");
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

        /// <summary>null = نطاق جديد لسه ماتحفظش؛ رقم = نطاق موجود من رصيد بيتعدّل</summary>
        public int? Id { get; set; }
        public IReadOnlyList<BalanceStageChoice> Stages { get; }
        public BalanceStageChoice? FromStage { get; set; }
        public BalanceStageChoice? ToStage { get; set; }
        public string QuantityText { get; set; } = "";

        /// <summary>كام قطعة اتاخدت من النطاق ده بالفعل — 0 لنطاق جديد أو نطاق موجود لسه مالوش استخدام</summary>
        public int UsedQuantity { get; set; }

        /// <summary>عليه استخدام؟ الامتداد (من/لمرحلة) وزرار الحذف بيتقفلوا لو true — شوف InitialBalanceService.EditAsync</summary>
        public bool IsLocked => UsedQuantity > 0;

        /// <summary>عكس IsLocked — تسهيلًا للـ Binding على IsEnabled في الـ XAML</summary>
        public bool IsEditableSpan => !IsLocked;
    }
}
