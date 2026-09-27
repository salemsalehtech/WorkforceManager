using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// آخر مراجعة قبل توقيع اليوم — بتعرض كل حدث اتسجّل في سجل العمليات
    /// النهارده (نفس <see cref="ActivityEventRow"/> اللي شاشة "سجل
    /// العمليات" بتستخدمها، عشان النص/الأيقونة/الترتيب يبقوا متطابقين)،
    /// زائد قايمة اكتمال البيانات (checklist) — إفادة بس، شوف تعليق
    /// ChecklistAcknowledge أسفل. شكلي بس — التوقيع نفسه في
    /// DailyOperationsSignOffService.
    /// </summary>
    public partial class DailySignOffSummaryDialog : Window
    {
        public DailySignOffSummaryDialog(DateTime date, IReadOnlyList<ActivityEvent> events, DailySignOffChecklist checklist)
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

            ProductsGapText.Text = "منتجات من غير إنتاج النهارده: " + string.Join("، ", checklist.ProductsWithNoEntries);
            ProductsGapBox.Visibility = checklist.ProductsWithNoEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            WorkersGapText.Text = "عمال من غير حضور النهارده: " + string.Join("، ", checklist.WorkersWithNoAttendance);
            WorkersGapBox.Visibility = checklist.WorkersWithNoAttendance.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            MemoryGapText.Text = "منتجات مخطط لها في الذاكرة النهارده ومدخلش لها إنتاج: " + string.Join("، ", checklist.MemoryDueWithNoEntries);
            MemoryGapBox.Visibility = checklist.MemoryDueWithNoEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            ChecklistClean.Visibility = checklist.HasAnyIssues ? Visibility.Collapsed : Visibility.Visible;

            // إفادة بس مش حجب — لازم تأكيد صريح إن النواقص دي متعمدة قبل
            // ما زرار التوقيع يشتغل، لكن مفيش يوم ممكن "يتعلّق" فيها؛ لو
            // القايمة نضيفة أصلًا الزرار شغال من غير أي تأكيد إضافي
            ChecklistAcknowledge.Visibility = checklist.HasAnyIssues ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = !checklist.HasAnyIssues;
        }

        private void ChecklistAcknowledge_Checked(object sender, RoutedEventArgs e) => ConfirmButton.IsEnabled = true;

        private void ChecklistAcknowledge_Unchecked(object sender, RoutedEventArgs e) => ConfirmButton.IsEnabled = false;

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
