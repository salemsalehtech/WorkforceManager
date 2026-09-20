using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// تسجيل كلمة سر العمليات أو تغييرها — من شاشة الإعدادات بس.
    ///
    /// النافذة دي بتجمع المدخلات وبتتأكد إن التأكيد مطابق. التحقق من
    /// الكلمة القديمة والتشفير في OperationsPasswordService — الشاشة
    /// عمرها ما بتشوف hash ولا بتقارن كلمة سر.
    /// </summary>
    public partial class OperationsPasswordDialog : Window
    {
        private OperationsPasswordDialog(bool requiresCurrent)
        {
            InitializeComponent();

            TitleText.Text = requiresCurrent ? "تغيير كلمة سر العمليات" : "تسجيل كلمة سر العمليات";
            CurrentSection.Visibility = requiresCurrent ? Visibility.Visible : Visibility.Collapsed;
        }

        private string _current = string.Empty;
        private string _new = string.Empty;

        /// <summary>
        /// بيعرض النافذة ويرجّع المدخلات، أو null لو المستخدم لغى.
        /// </summary>
        /// <param name="requiresCurrent">
        /// true لما يكون فيه كلمة سر متسجّلة بالفعل — تغييرها لازم يعدّي
        /// على القديمة، وإلا أي حد يقعد على الجهاز يغيّرها ويعدّي البوابة.
        /// </param>
        public static OperationsPasswordInput? Ask(Window? owner, bool requiresCurrent)
        {
            var dialog = new OperationsPasswordDialog(requiresCurrent);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true
                ? new OperationsPasswordInput(dialog._current, dialog._new)
                : null;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var newPassword = NewBox.Password;

            if (newPassword.Trim().Length < OperationsPasswordService.MinPasswordLength)
            {
                ShowError($"كلمة السر لازم تكون {OperationsPasswordService.MinPasswordLength} حروف/أرقام على الأقل");
                NewBox.Focus();
                return;
            }

            if (newPassword != ConfirmBox.Password)
            {
                ShowError("التأكيد مش مطابق لكلمة السر الجديدة");
                ConfirmBox.Clear();
                ConfirmBox.Focus();
                return;
            }

            _current = CurrentBox.Password;
            _new = newPassword;
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

    /// <summary>مدخلات نافذة كلمة سر العمليات</summary>
    /// <param name="CurrentPassword">الحالية (فاضية لو أول تسجيل)</param>
    /// <param name="NewPassword">الجديدة</param>
    public record OperationsPasswordInput(string CurrentPassword, string NewPassword);
}
