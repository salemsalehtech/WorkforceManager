using System.Windows;
using System.Windows.Controls;
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
    /// <summary>
    /// العملية اللي بيتأكّد منها: بتمسح حاجة، ولا بتحفظ/تسجّل حاجة.
    ///
    /// **الفرق ده كان ناقص**، فالنافذة كانت بتظهر بنفس الرأس الأحمر
    /// ونفس زرار "أكّد الحذف" على الاتنين — يعني اللي بيحفظ رحلة إنتاج
    /// كان بيتطلب منه يأكّد **حذف**. البوابة نفسها واحدة (كلمة السر)،
    /// بس اللي بتقوله للمستخدم لازم يطابق اللي هيحصل.
    /// </summary>
    public enum SensitiveActionKind
    {
        /// <summary>بتشيل بيانات — أحمر، وبيطلب سبب مكتوب</summary>
        Delete,

        /// <summary>بتحفظ أو تسجّل — بلون الهوية، ومفيش كلام عن حذف</summary>
        Save
    }

    public partial class SensitiveActionDialog : Window
    {
        private readonly bool _reasonRequired;
        private readonly SensitiveActionKind _kind;

        private SensitiveActionDialog(
            string title, string subtitle, SensitiveActionKind kind,
            bool passwordRequired, bool reasonRequired, bool reasonOptionalVisible)
        {
            InitializeComponent();

            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            _reasonRequired = reasonRequired;
            _kind = kind;

            ApplyKind(kind);

            // مفيش كلمة سر متسجّلة: بنخفي الخانة وبنوضّح السبب بدل ما
            // نطلب من المستخدم حاجة مش موجودة أصلاً
            PasswordSection.Visibility = passwordRequired ? Visibility.Visible : Visibility.Collapsed;
            NotConfiguredBox.Visibility = passwordRequired ? Visibility.Collapsed : Visibility.Visible;

            // العمليات المتكررة (حفظ الحضور) بتاخد كلمة سر بس: "اكتب سبب"
            // على شغل يومي بيتحوّل لخانة المستخدم بيكتب فيها نقطة.
            // reasonOptionalVisible بتعرض الخانة من غير ما تفرض تعبئتها —
            // لعمليات زي تغيير عامل سجل إنتاج: السبب مفيد بس مش إجباري
            var showReason = reasonRequired || reasonOptionalVisible;
            ReasonSection.Visibility = showReason ? Visibility.Visible : Visibility.Collapsed;

            if (showReason && !reasonRequired)
                ReasonLabel.Text += " (اختياري)";
        }

        /// <summary>
        /// كل اللي بيفرّق بين الحذف والحفظ في مكان واحد: اللون، ونص
        /// الزرار، وعنوان خانة السبب، وعنوان النافذة.
        /// </summary>
        private void ApplyKind(SensitiveActionKind kind)
        {
            var deleting = kind == SensitiveActionKind.Delete;

            Title = deleting ? "تأكيد حذف" : "تأكيد عملية";

            HeaderBar.SetResourceReference(
                Border.BackgroundProperty, deleting ? "DangerBrush" : "SidebarBrush");

            ConfirmButton.Content = deleting ? "أكّد الحذف" : "أكّد واحفظ";
            ConfirmButton.SetResourceReference(
                StyleProperty, deleting ? "DangerButton" : "PrimaryButton");

            ReasonLabel.Text = deleting ? "سبب الحذف" : "السبب / ملاحظة";
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
        /// <param name="reasonOptionalVisible">
        /// true بيعرض خانة السبب من غير ما يفرض تعبئتها — لعمليات
        /// حساسة (زي تغيير عامل سجل إنتاج) السبب مفيد للمراجعة لاحقًا
        /// بس مش إجباري. مالوش أثر لو <paramref name="reasonRequired"/> = true.
        /// </param>
        /// <param name="kind">
        /// حذف ولا حفظ. **إجباري عن قصد ومفيش قيمة افتراضية**: النافذة
        /// دي شغلها إن المستخدم يفهم هو بيوافق على إيه، فاختيار غلط
        /// بالسكوت أسوأ من خطأ ترجمة.
        /// </param>
        public static SensitiveActionInput? Ask(
            Window? owner, string title, string subtitle, SensitiveActionKind kind,
            bool passwordRequired, bool reasonRequired = true, bool reasonOptionalVisible = false)
        {
            var dialog = new SensitiveActionDialog(
                title, subtitle, kind, passwordRequired, reasonRequired, reasonOptionalVisible);
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
                ShowError(_kind == SensitiveActionKind.Delete
                    ? "لازم تكتب سبب الحذف"
                    : "لازم تكتب سبب العملية");
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
            ErrorBox.ShowError(ErrorText, message);
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
