using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// ترتيب العمال المخصص — نفس فكرة ترتيب مراحل المنتج بالظبط
    /// (<see cref="ProductManagementService.MoveStageAsync"/>)، بس هنا
    /// على مستوى العمال كلهم مش خط منتج واحد. الترتيب ده هو اللي كل
    /// شاشة بتعرض عمال بيها بدل الترتيب الأبجدي (Worker.SortOrder).
    ///
    /// تلات طرق للترتيب، كلها بتحفظ فورًا:
    ///   • زرارين ▲▼ — نقلة واحدة، بينادوا MoveWorkerAsync (تبديل مع الجار)
    ///   • سحب وإفلات — نقلة كبيرة بضغطة واحدة
    ///   • كتابة رقم الترتيب — نفس فكرة السحب، للي مش عايز يسحب
    /// السحب والكتابة بينادوا ReorderAsync (ترتيب كامل دفعة واحدة) —
    /// نقلة من مكان 40 لمكان 2 هتبقى عملية واحدة مش 38 تبديلة.
    /// </summary>
    public partial class WorkerOrderDialog : Window
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private Point _dragStart;

        private WorkerOrderDialog(IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// بيجهّز القايمة ويعرضها. بيرجع بعد ما المستخدم يقفل النافذة —
        /// المتصل مسؤول عن إعادة تحميل شاشته هو (زي أي دايالوج تعديل
        /// تاني في البرنامج)، لأن الترتيب ممكن يتغيّر أكتر من مرة جوّه.
        /// </summary>
        public static async Task ShowAsync(Window? owner, IServiceScopeFactory scopeFactory)
        {
            var dialog = new WorkerOrderDialog(scopeFactory);
            if (owner is not null) dialog.Owner = owner;

            await dialog.ReloadAsync();
            dialog.ShowDialog();
        }

        private async Task ReloadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var workers = await scope.ServiceProvider.GetRequiredService<IWorkerRepository>()
                .GetAllWithSkillsAsync();

            WorkersList.ItemsSource = workers
                .Select((w, index) => new WorkerOrderRow { WorkerId = w.Id, FullName = w.FullName, Rank = index + 1 })
                .ToList();
        }

        // ======================= الزرارين =======================

        private async void MoveUp_Click(object sender, RoutedEventArgs e) => await MoveAsync(sender, moveUp: true);
        private async void MoveDown_Click(object sender, RoutedEventArgs e) => await MoveAsync(sender, moveUp: false);

        private async Task MoveAsync(object sender, bool moveUp)
        {
            if (((FrameworkElement)sender).DataContext is not WorkerOrderRow row) return;

            using (var scope = _scopeFactory.CreateScope())
            {
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                // القاعدة نفسها في الخدمة — الشاشة بتطلب الحركة بس
                await mgmt.MoveWorkerAsync(row.WorkerId, moveUp);
            }

            await ReloadAsync();
        }

        // ======================= كتابة رقم الترتيب =======================

        private void RankBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter بيشيل التركيز، وده بيطلق LostFocus اللي بيطبّق الرقم —
            // مسار واحد للتطبيق (زي ما تدوس Enter أو تخرج من الخانة بالماوس)
            if (e.Key == Key.Enter) Keyboard.ClearFocus();
        }

        private async void RankBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box) await ApplyTypedRankAsync(box);
        }

        private async Task ApplyTypedRankAsync(TextBox box)
        {
            if (box.DataContext is not WorkerOrderRow row) return;
            if (WorkersList.ItemsSource is not List<WorkerOrderRow> rows) return;

            var oldIndex = rows.IndexOf(row);
            if (oldIndex < 0) return; // القايمة اتحمّلت من جديد من تحته

            if (!int.TryParse(box.Text, out var typed))
            {
                box.Text = row.Rank.ToString();
                return;
            }

            // رقم برّه المدى بيتصحّح لأقرب طرف بدل ما يترفض — أسهل من
            // رسالة خطأ على حاجة بسيطة زي ترتيب
            var newIndex = Math.Clamp(typed - 1, 0, rows.Count - 1);
            if (newIndex == oldIndex)
            {
                box.Text = row.Rank.ToString();
                return;
            }

            rows.RemoveAt(oldIndex);
            rows.Insert(newIndex, row);

            await PersistOrderAsync(rows);
        }

        // ======================= السحب والإفلات =======================

        private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // خانة الرقم ليها تعاملها الخاص (كتابة/تحديد نص) — مينفعش
            // نبدأ سحب للصف كله وهي اللي بتاخد الكليك
            if (IsInsideTextBox(e.OriginalSource as DependencyObject)) return;

            _dragStart = e.GetPosition(null);
        }

        private void Row_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (IsInsideTextBox(e.OriginalSource as DependencyObject)) return;
            if (sender is not FrameworkElement { DataContext: WorkerOrderRow row } element) return;

            var current = e.GetPosition(null);
            var diff = _dragStart - current;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            DragDrop.DoDragDrop(element, row, DragDropEffects.Move);
        }

        private void Row_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border) border.SetResourceReference(Border.BackgroundProperty, "SelectionBgBrush");
        }

        private void Row_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border) border.SetResourceReference(Border.BackgroundProperty, "SubtleBgBrush");
        }

        private async void Row_Drop(object sender, DragEventArgs e)
        {
            Row_DragLeave(sender, e); // يرجّع الخلفية العادية حتى لو الإفلات فشل تحت

            if (sender is not FrameworkElement { DataContext: WorkerOrderRow targetRow }) return;
            if (e.Data.GetData(typeof(WorkerOrderRow)) is not WorkerOrderRow draggedRow) return;
            if (ReferenceEquals(draggedRow, targetRow)) return;
            if (WorkersList.ItemsSource is not List<WorkerOrderRow> rows) return;

            var oldIndex = rows.IndexOf(draggedRow);
            var newIndex = rows.IndexOf(targetRow);
            if (oldIndex < 0 || newIndex < 0) return;

            rows.RemoveAt(oldIndex);
            rows.Insert(newIndex, draggedRow);

            await PersistOrderAsync(rows);
        }

        private static bool IsInsideTextBox(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is TextBox) return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        // ======================= حفظ مشترك =======================

        private async Task PersistOrderAsync(List<WorkerOrderRow> rows)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                await mgmt.ReorderAsync(rows.Select(r => r.WorkerId).ToList());
            }

            await ReloadAsync();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }

    /// <summary>سطر عامل واحد في شاشة الترتيب — تنسيق عرض بس</summary>
    public class WorkerOrderRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public int Rank { get; init; }
    }
}
