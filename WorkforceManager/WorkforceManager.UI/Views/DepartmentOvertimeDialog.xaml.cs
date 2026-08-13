using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// تصحيح يوم حضور/سهر لحساب إداري: يوم + حالة (حاضر بشيفت معيّن،
    /// أو غياب بإذن/بدونه). الشاشة الأم بتفسّر القيم دي وتنادي
    /// HourlyWorkdayService.RecordHourlyWorkAsync (حاضر) أو تسجّل غياب
    /// مباشرة (بعد ما تشيل أي سجل شغل بالساعة لليوم ده لو موجود —
    /// نفس قاعدة "الغياب مع شغل مسجّل ممنوع" في AttendanceService).
    /// </summary>
    public partial class DepartmentOvertimeDialog : Window
    {
        private record StatusOption(AttendanceStatus Status, string Label);

        // ShiftPresets عبارة عن (int EndHour24, string Label) — أسماء
        // عناصر الـ tuple دي معروفة وقت الكومبايل بس (TupleElementNames)،
        // والـ runtime type الفعلي بتاعها ValueTuple بخصائص Item1/Item2.
        // DisplayMemberPath/SelectedValuePath بيدوروا بالـ reflection على
        // اسم حقيقي، فبيرجعوا فاضي مع tuple مباشر — لازم نلفّه في نوع
        // حقيقي (نفس اللي AttendanceRow.ShiftChoice بيعمله بالظبط)
        private record ShiftOption(int EndHour24, string Label);

        public DepartmentOvertimeDialog(string accountName, HourlyRole role)
        {
            InitializeComponent();

            AccountNameText.Text = $"تصحيح يوم لـ \"{accountName}\".";

            var statusOptions = new List<StatusOption>
            {
                new(AttendanceStatus.Present, "حاضر"),
                new(AttendanceStatus.AbsentWithPermission, "غائب بإذن")
            };

            // "غياب بدون إذن" معناه حد فوقه بيحاسبه على الغياب ده —
            // ومفيش حد فوق مدير القسم في التسلسل الإداري، فالخيار ده
            // مالوش معنى لحسابه (رئيس القسم لسه تحته المدير)
            if (role != HourlyRole.DepartmentManager)
                statusOptions.Add(new StatusOption(AttendanceStatus.AbsentWithoutPermission, "غائب بدون إذن"));

            StatusBox.ItemsSource = statusOptions;
            StatusBox.SelectedIndex = 0;

            ShiftBox.ItemsSource = HourlyWorkdayService.ShiftPresets
                .Select(p => new ShiftOption(p.EndHour24, p.Label))
                .ToList();
            ShiftBox.SelectedIndex = 0;

            DayPicker.SelectedDate = DateTime.Today;
        }

        public DateTime Day => DayPicker.SelectedDate ?? DateTime.Today;
        public AttendanceStatus Status => (AttendanceStatus)(StatusBox.SelectedValue ?? AttendanceStatus.Present);
        public int EndHour24 => (int)(ShiftBox.SelectedValue ?? HourlyWorkdayService.ShiftEndHour);

        private void StatusBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // خانة الشيفت مالهاش معنى غير لما يكون حاضر
            ShiftPanel.Visibility = Status == AttendanceStatus.Present
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
