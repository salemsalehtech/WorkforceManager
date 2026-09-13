using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// آخر مراجعة قبل توقيع اليوم — بتعرض كل حدث اتسجّل في سجل العمليات
    /// النهارده (نفس <see cref="ActivityEventRow"/> اللي شاشة "سجل
    /// العمليات" بتستخدمها، عشان النص/الأيقونة/الترتيب يبقوا متطابقين).
    /// شكلي بس — التوقيع نفسه في DailyOperationsSignOffService.
    /// </summary>
    public partial class DailySignOffSummaryDialog : Window
    {
        public DailySignOffSummaryDialog(DateTime date, IReadOnlyList<ActivityEvent> events)
        {
            InitializeComponent();

            DateText.Text = date.ToString("dddd yyyy/MM/dd");

            var rows = events
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => new ActivityEventRow(e))
                .ToList();

            CountText.Text = rows.Count > 0
                ? $"{rows.Count} عملية اتسجلت النهارده"
                : "مفيش أي عمليات اتسجلت النهارده";

            EventsList.ItemsSource = rows;
            EventsList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NothingHappened.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
