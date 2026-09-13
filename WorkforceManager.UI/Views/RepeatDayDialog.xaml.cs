using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// اختيار اليوم اللي توزيع العمال هيتنسخ منه.
    ///
    /// بيعرض الأيام اللي فيها شغل فعلي على المنتج بس. منتقي تاريخ عادي
    /// كان هيخلي المستخدم يجرّب أيام فاضية لحد ما يلاقي واحد فيه حاجة —
    /// والقايمة كمان بتوريه كل يوم فيه كام عامل، فيعرف يختار.
    /// </summary>
    public partial class RepeatDayDialog : Window
    {
        private RepeatDayDialog(string productName, IReadOnlyList<FlowDayOptionDto> days)
        {
            InitializeComponent();

            SubtitleText.Text = $"\"{productName}\" — اختار اليوم اللي هتنسخ توزيعه";

            DaysList.ItemsSource = days;

            var empty = days.Count == 0;
            EmptyText.Text = $"مفيش أي إنتاج متسجل على \"{productName}\" في آخر شهرين — مفيش يوم يتكرر.";
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ListScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>اليوم اللي المستخدم اختاره (null = قفل من غير اختيار)</summary>
        public DateTime? PickedDate { get; private set; }

        /// <summary>بيعرض القايمة ويرجّع اليوم المختار، أو null لو المستخدم لغى</summary>
        public static DateTime? Pick(
            Window? owner, string productName, IReadOnlyList<FlowDayOptionDto> days)
        {
            var dialog = new RepeatDayDialog(productName, days);
            if (owner is not null) dialog.Owner = owner;

            dialog.ShowDialog();
            return dialog.PickedDate;
        }

        private void PickDay_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not FlowDayOptionDto day) return;

            PickedDate = day.Date;
            DialogResult = true;
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
