using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تأكيد العملية الحساسة — **الواجهة الوحيدة** اللي بتاخد كلمة
    /// سر العمليات وسبب الحذف.
    ///
    /// أي شاشة عايزة تحذف بتنادي <see cref="Ask"/> وبتاخد النتيجة. ممنوع
    /// أي شاشة تعمل نافذة كلمة سر بتاعتها — لأن ساعتها نص التحذير وقواعد
    /// العرض هيختلفوا من شاشة للتانية.
    ///
    /// النافذة دي بتجمع المدخلات بس، **مش** بتتحقق من كلمة السر: التحقق
    /// في OperationsPasswordService عشان قاعدة القفل بعد المحاولات الغلط
    /// تفضل في مكان واحد.
    /// </summary>
    public partial class SensitiveActionDialog : Window
    {
        private readonly bool _reasonRequired;

        private SensitiveActionDialog(
            string title, string subtitle, bool passwordRequired, bool reasonRequired)
        {
            InitializeComponent();

            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            _reasonRequired = reasonRequired;

            // مفيش كلمة سر متسجّلة: بنخفي الخانة وبنوضّح السبب بدل ما
            // نطلب من المستخدم حاجة مش موجودة أصلاً
            PasswordSection.Visibility = passwordRequired ? Visibility.Visible : Visibility.Collapsed;
            NotConfiguredBox.Visibility = passwordRequired ? Visibility.Collapsed : Visibility.Visible;

            // العمليات المتكررة (حفظ الحضور) بتاخد كلمة سر بس: "اكتب سبب"
            // على شغل يومي بيتحوّل لخانة المستخدم بيكتب فيها نقطة
            ReasonSection.Visibility = reasonRequired ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>كلمة السر اللي كتبها المستخدم (فاضية لو مفيش واحدة متسجّلة)</summary>
        public string EnteredPassword { get; private set; } = string.Empty;

        /// <summary>سبب الحذف اللي كتبه المستخدم</summary>
        public string EnteredReason { get; private set; } = string.Empty;

        /// <summary>
        /// بيعرض النافذة ويرجّع المدخلات، أو null لو المستخدم لغى.
        /// </summary>
        /// <param name="title">اسم العملية ("حذف عامل")</param>
        /// <param name="subtitle">اللي هيتشال بالظبط ("أحمد محمد")</param>
        /// <param name="passwordRequired">
        /// false لما مفيش كلمة سر متسجّلة — النافذة بتفضل بتطلب السبب
        /// لأن السجل من غير سبب مالوش قيمة، بس مش بتطلب كلمة سر.
        /// </param>
        /// <param name="reasonRequired">
        /// false للعمليات المتكررة اللي مش بتمسح حاجة (حفظ الحضور):
        /// كلمة سر بس من غير سبب مكتوب.
        /// </param>
        public static SensitiveActionInput? Ask(
            Window? owner, string title, string subtitle,
            bool passwordRequired, bool reasonRequired = true)
        {
            var dialog = new SensitiveActionDialog(title, subtitle, passwordRequired, reasonRequired);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true
                ? new SensitiveActionInput(dialog.EnteredPassword, dialog.EnteredReason)
                : null;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var reason = ReasonBox.Text.Trim();
            if (_reasonRequired && reason.Length == 0)
            {
                ShowError("لازم تكتب سبب الحذف");
                ReasonBox.Focus();
                return;
            }

            EnteredPassword = PasswordBox.Password;
            EnteredReason = reason;
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

    /// <summary>مدخلات المستخدم من نافذة التأكيد</summary>
    /// <param name="Password">كلمة سر العمليات</param>
    /// <param name="Reason">سبب الحذف</param>
    public record SensitiveActionInput(string Password, string Reason);
}
