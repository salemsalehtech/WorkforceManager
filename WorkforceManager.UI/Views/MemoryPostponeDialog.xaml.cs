using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// اختيار يوم جديد لتذكير مؤجّل.
    ///
    /// اليوم لازم يكون بكرة أو بعده: تأجيل لليوم الحالي أو قبله معناه
    /// إن التذكير هيضرب تاني أول تشغيل جاي — يعني المستخدم ماأجّلش حاجة.
    /// </summary>
    public partial class MemoryPostponeDialog : Window
    {
        private MemoryPostponeDialog(DateTime current)
        {
            InitializeComponent();

            // نقطة بداية معقولة: بكرة، أو اليوم اللي بعد التذكير الحالي
            // لو كان في المستقبل أصلاً
            DatePick.SelectedDate = current.Date > DateTime.Today
                ? current.Date.AddDays(1)
                : DateTime.Today.AddDays(1);
        }

        /// <summary>بيرجّع اليوم الجديد، أو null لو المستخدم لغى</summary>
        public static DateTime? Ask(Window? owner, DateTime current)
        {
            var dialog = new MemoryPostponeDialog(current);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true ? dialog.DatePick.SelectedDate!.Value.Date : null;
        }

        private void Quick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var days)) return;

            DatePick.SelectedDate = DateTime.Today.AddDays(days);
            Confirm_Click(sender, e);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (DatePick.SelectedDate is not { } picked)
            {
                ShowError("اختار يوم الأول");
                return;
            }

            if (picked.Date <= DateTime.Today)
            {
                ShowError("اختار يوم بعد النهارده — غير كده التذكير هيرجع يظهر على طول");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
