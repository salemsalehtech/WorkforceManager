using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>ردّ المستخدم على تذكير خطة</summary>
    public enum MemoryReminderChoice
    {
        /// <summary>سابها زي ما هي — هتظهر تاني أول تشغيل جاي</summary>
        Dismissed,

        /// <summary>ابدأ التسجيل دلوقتي</summary>
        Start,

        /// <summary>أجّل ليوم تاني (<see cref="MemoryReminderDialog.NewRemindOn"/>)</summary>
        Postpone
    }

    /// <summary>
    /// تذكير خطة الذاكرة عند بدء التشغيل.
    ///
    /// **بيتعرض واحد ورا التاني** لو فيه أكتر من خطة مستحقة — قرار
    /// مؤكد مع المستخدم: نوافذ مكوّمة فوق بعض بتخلي المستخدم يقفلهم كلهم
    /// من غير ما يقرا ولا واحدة.
    /// </summary>
    public partial class MemoryReminderDialog : Window
    {
        private readonly ProductionMemoryDto _memory;

        /// <summary>اليوم الجديد لو المستخدم اختار التأجيل</summary>
        public DateTime NewRemindOn { get; private set; }

        public MemoryReminderChoice Choice { get; private set; } = MemoryReminderChoice.Dismissed;

        private MemoryReminderDialog(ProductionMemoryDto memory)
        {
            InitializeComponent();

            _memory = memory;

            ProductText.Text = memory.ProductName;
            StagesList.ItemsSource = memory.Stages;

            var late = (DateTime.Today - memory.RemindOn.Date).Days;
            DueText.Text = late switch
            {
                <= 0 => "التذكير بتاع النهارده",
                1 => "كان من إمبارح",
                _ => $"كان من {late} يوم"
            };

            if (string.IsNullOrWhiteSpace(memory.Notes))
                NotesBox.Visibility = Visibility.Collapsed;
            else
                NotesText.Text = memory.Notes;

            // المنتج اتوقف أو اتشال، أو مرحلة من الخطة بقت مش في الخط
            if (memory.IsBlocked)
            {
                BlockedText.Text = memory.BlockedReason;
                BlockedBox.Visibility = Visibility.Visible;
                StartButton.IsEnabled = false;
                StartButton.ToolTip = memory.BlockedReason;
            }
        }

        /// <summary>بيعرض التذكير ويرجّع اختيار المستخدم</summary>
        public static MemoryReminderDialog Show(Window? owner, ProductionMemoryDto memory)
        {
            var dialog = new MemoryReminderDialog(memory);

            // ممكن يتعرض قبل ما النافذة الرئيسية تبان — و Owner على نافذة
            // لسه ماتعرضتش بترمي (نفس حالة MessageDialog)
            if (owner is not null && owner.IsLoaded)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            dialog.ShowDialog();
            return dialog;
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            Choice = MemoryReminderChoice.Start;
            DialogResult = true;
            Close();
        }

        private void Postpone_Click(object sender, RoutedEventArgs e)
        {
            var picked = MemoryPostponeDialog.Ask(this, _memory.RemindOn);
            if (picked is null) return; // لغى اختيار اليوم — التذكير لسه معروض

            NewRemindOn = picked.Value;
            Choice = MemoryReminderChoice.Postpone;
            DialogResult = true;
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
