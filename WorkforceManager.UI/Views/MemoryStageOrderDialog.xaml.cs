using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// ترتيب مراحل خطة الذاكرة — نفس تفاعل
    /// <see cref="WorkerOrderDialog"/> (سحب وإفلات + زرارين ▲▼) بفرقين:
    ///
    ///   • **مفيش حفظ فوري**: الترتيب بيتبنى في الذاكرة وبيترجّع للمنادي،
    ///     لأن الخطة نفسها ممكن تكون لسه ما اتحفظتش. الدايالوج ده مالوش
    ///     أي علاقة بقاعدة البيانات خالص.
    ///   • **ينفع تشيل مرحلة**: الخطة ممكن تتخطى مراحل عن قصد، فالترقيم
    ///     بيتحسب على المختارة بس.
    /// </summary>
    public partial class MemoryStageOrderDialog : Window
    {
        private readonly List<StageOrderRow> _rows;
        private Point _dragStart;

        private MemoryStageOrderDialog(List<StageOrderRow> rows)
        {
            InitializeComponent();

            _rows = rows;
            StagesList.ItemsSource = _rows;

            foreach (var row in _rows)
                row.PropertyChanged += (_, e) =>
                {
                    // شيلة/إضافة مرحلة بتغيّر ترقيم اللي بعدها كله
                    if (e.PropertyName == nameof(StageOrderRow.IsIncluded)) Renumber();
                };

            Renumber();
        }

        /// <summary>
        /// بيعرض النافذة ويرجّع معرّفات المراحل المختارة **بترتيبها**،
        /// أو null لو المستخدم لغى.
        /// </summary>
        /// <param name="lineStages">مراحل خط المنتج النشط بترتيبها الحقيقي</param>
        /// <param name="selected">
        /// الترتيب الحالي للخطة (فاضي لخطة جديدة — ساعتها كل المراحل
        /// مختارة بترتيب المنتج، وهو المتوقع كنقطة بداية).
        /// </param>
        public static IReadOnlyList<int>? Ask(
            Window? owner,
            IReadOnlyList<(int StageId, string StageName)> lineStages,
            IReadOnlyList<int> selected)
        {
            var rows = BuildRows(lineStages, selected);

            var dialog = new MemoryStageOrderDialog(rows);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true
                ? rows.Where(r => r.IsIncluded).Select(r => r.StageId).ToList()
                : null;
        }

        /// <summary>
        /// المختارة بترتيب الخطة الأول، وبعدين اللي اتشالت بترتيب المنتج —
        /// كده المستخدم بيشوف خطته زي ما سابها، والمشيول متاح يرجّعه.
        /// </summary>
        private static List<StageOrderRow> BuildRows(
            IReadOnlyList<(int StageId, string StageName)> lineStages, IReadOnlyList<int> selected)
        {
            var nameById = lineStages.ToDictionary(s => s.StageId, s => s.StageName);
            var rows = new List<StageOrderRow>();

            foreach (var stageId in selected)
                if (nameById.TryGetValue(stageId, out var name))
                    rows.Add(new StageOrderRow { StageId = stageId, StageName = name, IsIncluded = true });

            var already = rows.Select(r => r.StageId).ToHashSet();

            foreach (var (stageId, name) in lineStages)
                if (!already.Contains(stageId))
                    rows.Add(new StageOrderRow
                    {
                        StageId = stageId,
                        StageName = name,
                        // خطة جديدة (مفيش اختيار سابق) بتبدأ بكل المراحل مختارة
                        IsIncluded = selected.Count == 0
                    });

            return rows;
        }

        private void Renumber()
        {
            var rank = 0;

            foreach (var row in _rows)
                row.Rank = row.IsIncluded ? ++rank : 0;

            SummaryText.Text = rank == _rows.Count
                ? $"{rank} مرحلة"
                : $"{rank} مرحلة من {_rows.Count} — {_rows.Count - rank} مشيلة من الخطة";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (!_rows.Any(r => r.IsIncluded))
            {
                ErrorText.Text = "اختار مرحلة واحدة على الأقل — الخطة مالهاش معنى من غير مراحل";
                ErrorBox.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        }

        // ======================= الزرارين =======================

        private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, +1);

        private void Move(object sender, int delta)
        {
            if (((FrameworkElement)sender).DataContext is not StageOrderRow row) return;

            var oldIndex = _rows.IndexOf(row);
            var newIndex = oldIndex + delta;

            // عند الطرف مفيش حركة — بيسكت بدل ما يرمي، زي MoveStageAsync
            if (oldIndex < 0 || newIndex < 0 || newIndex >= _rows.Count) return;

            MoveRow(oldIndex, newIndex);
        }

        // ======================= السحب والإفلات =======================

        private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _dragStart = e.GetPosition(null);

        private void Row_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not FrameworkElement { DataContext: StageOrderRow row } element) return;

            var diff = _dragStart - e.GetPosition(null);

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            DragDrop.DoDragDrop(element, row, DragDropEffects.Move);
        }

        private void Row_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border) border.SetResourceReference(BackgroundProperty, "SelectionBgBrush");
        }

        private void Row_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border) border.SetResourceReference(BackgroundProperty, "SubtleBgBrush");
        }

        private void Row_Drop(object sender, DragEventArgs e)
        {
            Row_DragLeave(sender, e); // ترجيع الخلفية حتى لو الإفلات مكملش

            if (sender is not FrameworkElement { DataContext: StageOrderRow target }) return;
            if (e.Data.GetData(typeof(StageOrderRow)) is not StageOrderRow dragged) return;
            if (ReferenceEquals(dragged, target)) return;

            MoveRow(_rows.IndexOf(dragged), _rows.IndexOf(target));
        }

        /// <summary>
        /// النقل الفعلي. ItemsControl مش بيلاحظ إعادة ترتيب القايمة
        /// نفسها، فبنعيد ربطها — القايمة صغيرة (مراحل منتج واحد) فمفيش
        /// داعي لـ ObservableCollection ونقل داخلي.
        /// </summary>
        private void MoveRow(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

            var row = _rows[oldIndex];
            _rows.RemoveAt(oldIndex);
            _rows.Insert(newIndex, row);

            StagesList.ItemsSource = null;
            StagesList.ItemsSource = _rows;

            Renumber();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }

    /// <summary>سطر مرحلة واحدة في نافذة الترتيب — تنسيق عرض بس</summary>
    public partial class StageOrderRow : ObservableObject
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";

        /// <summary>داخلة في الخطة؟ شيلها والترقيم بيتعدّل لوحده</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NameOpacity))]
        [NotifyPropertyChangedFor(nameof(RankVisibility))]
        private bool _isIncluded = true;

        /// <summary>موقعها بين المراحل المختارة (صفر = مشيلة)</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RankVisibility))]
        private int _rank;

        public Visibility RankVisibility => IsIncluded ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>المرحلة المشيلة باهتة — موجودة عشان ترجّعها، مش جزء من الخطة</summary>
        public double NameOpacity => IsIncluded ? 1.0 : 0.45;
    }
}
