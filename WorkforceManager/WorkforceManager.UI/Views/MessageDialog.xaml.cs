using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نوع الرسالة — بيحدّد اللون والأيقونة ونمط زرار التأكيد.
    ///
    /// **مفيش قيمة افتراضية عن قصد**: نفس سبب SensitiveActionKind — سؤال
    /// على حاجة مش بترجع لو ظهر بشكل خبر عادي، المستخدم بيدوس "أيوه"
    /// من غير ما ياخد باله.
    /// </summary>
    public enum MessageKind
    {
        /// <summary>سؤال عادي — بلون الهوية</summary>
        Question,

        /// <summary>سؤال على حاجة مش بترجع (حذف، استبدال) — أصفر تحذيري</summary>
        Warning,

        /// <summary>حاجة فشلت والمستخدم لازم يشوفها — أحمر</summary>
        Error,

        /// <summary>خبر بس — أزرق</summary>
        Info
    }

    /// <summary>
    /// ربط النوع بالشكل، في مكان واحد ومن غير أي WPF — عشان يتختبر.
    ///
    /// بيرجّع **أسامي موارد كنصوص** مش فرش جاهزة: نفس نمط ToastHost
    /// بالظبط، وكمان لأن الفرشاة الجاهزة بتتجمّد على ثيم واحد بينما
    /// SetResourceReference بتفضل حية لما الثيم يتقلب.
    /// </summary>
    public static class MessageAppearance
    {
        public static string Icon(MessageKind kind) => kind switch
        {
            MessageKind.Question => "HelpCircleOutline",
            MessageKind.Warning => "AlertOutline",
            MessageKind.Error => "AlertCircleOutline",
            _ => "InformationOutline"
        };

        /// <summary>
        /// خلفية الهيدر: **التظليل الباهت مش اللون المصمت**.
        ///
        /// ألوان الخطورة بتنقلب بين الثيمين (الأحمر #A0342A غامق في
        /// الفاتح و#E08A6E فاتح في الغامق)، بينما أي حبر ثابت بيفضل زي ما
        /// هو — فاللون المصمت معناه نص فاتح على خلفية فاتحة في الثيم
        /// الأسود. أزواج التظليل/اللون دي اللوحة معرّفاها مع بعض عشان
        /// الحالة دي بالظبط، ومستعملة مع بعض في SensitiveActionDialog.
        /// </summary>
        public static string HeaderBrush(MessageKind kind) => kind switch
        {
            MessageKind.Question => "GoldTintBrush",
            MessageKind.Warning => "WarnBgBrush",
            MessageKind.Error => "DangerBgBrush",
            _ => "InfoTintBrush"
        };

        /// <summary>لون العنوان والأيقونة — بيتقلب مع التظليل فوقه</summary>
        public static string HeaderInk(MessageKind kind) => kind switch
        {
            MessageKind.Question => "GoldDeepBrush",
            MessageKind.Warning => "WarnBrush",
            MessageKind.Error => "DangerBrush",
            _ => "InfoBrush"
        };

        /// <summary>التحذير بس هو اللي زراره أحمر — الباقي لون الهوية</summary>
        public static string ConfirmStyle(MessageKind kind) =>
            kind == MessageKind.Warning ? "DangerButton" : "PrimaryButton";
    }

    public partial class MessageDialog : Window
    {
        private MessageDialog(string message, string title, MessageKind kind,
            bool twoButtons, bool defaultIsNo)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;

            KindIcon.Kind = Enum.Parse<PackIconKind>(MessageAppearance.Icon(kind));
            HeaderBar.SetResourceReference(BackgroundProperty, MessageAppearance.HeaderBrush(kind));
            YesButton.SetResourceReference(StyleProperty, MessageAppearance.ConfirmStyle(kind));

            // الحبر بيتحط من الكود مش من الـ XAML عشان يتقلب مع التظليل
            var ink = MessageAppearance.HeaderInk(kind);
            foreach (var element in new FrameworkElement[] { TitleText, KindIcon, CloseButton })
                element.SetResourceReference(ForegroundProperty, ink);

            if (twoButtons)
            {
                YesButton.Content = "أيوه";

                // الافتراضي "لأ" للأفعال اللي مش بترجع: ضغطة Enter بالغلط
                // مالهاش حق تمسح شغل. الزرار الافتراضي هو اللي Enter بتدوسه.
                YesButton.IsDefault = !defaultIsNo;
                NoButton.IsDefault = defaultIsNo;
            }
            else
            {
                // خبر أو خطأ: مفيش قرار، زرار واحد بيقفل بس
                YesButton.Content = "تمام";
                YesButton.IsDefault = true;
                NoButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>سؤال أيوه/لأ — بيرجّع true لو المستخدم وافق</summary>
        /// <param name="defaultIsNo">
        /// true بيخلي Enter تدوس "لأ" — للأفعال اللي مش بترجع.
        /// </param>
        public static bool Ask(string message, string title, MessageKind kind, bool defaultIsNo) =>
            ShowCore(message, title, kind, twoButtons: true, defaultIsNo);

        /// <summary>خبر أو خطأ بزرار "تمام" واحد</summary>
        public static void Show(string message, string title, MessageKind kind) =>
            ShowCore(message, title, kind, twoButtons: false, defaultIsNo: false);

        private static bool ShowCore(string message, string title, MessageKind kind,
            bool twoButtons, bool defaultIsNo)
        {
            var app = Application.Current;

            // MessageBox.Show كان بيشتغل من أي خيط، والنافذة المخصصة لأ —
            // و Notify.Error بتتنادى من catch جوه عمليات بتشتغل في الخلفية،
            // فمن غير التحويل ده الرسالة اللي المفروض تحذّر بتبوظ هي كمان
            if (app is not null && !app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() =>
                    ShowCore(message, title, kind, twoButtons, defaultIsNo));

            var dialog = new MessageDialog(message, title, kind, twoButtons, defaultIsNo);

            // ممكن الرسالة تظهر قبل ما النافذة الرئيسية تتعرض أصلاً (زي
            // "البرنامج شغال بالفعل") — و Owner على نافذة لسه ماتعرضتش
            // بترمي، فبنتوسّط الشاشة بدل ما نتوسّط عليها
            var owner = app?.MainWindow;
            if (owner is not null && owner.IsLoaded)
                dialog.Owner = owner;
            else
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            return dialog.ShowDialog() == true;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
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
