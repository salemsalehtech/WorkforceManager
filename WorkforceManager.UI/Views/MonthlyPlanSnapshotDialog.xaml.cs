using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>عرض لقطة "لقطة نهاية اليوم" محفوظة — عرض بس، شوف تعليق XAML</summary>
    public partial class MonthlyPlanSnapshotDialog : Window
    {
        public MonthlyPlanSnapshotDialog(DateTime takenAt, IReadOnlyList<MonthlyPlanSnapshotRowDto> rows)
        {
            InitializeComponent();
            HeaderText.Text = $"لقطة {takenAt:yyyy/MM/dd HH:mm}";
            RowsList.ItemsSource = rows;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
