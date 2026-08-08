using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>عمليات الحساب اللي النافذة بتخدمها</summary>
    public enum AccountAction
    {
        ChangeUsername,
        ChangeLoginPassword,
        AddAccount
    }

    /// <summary>
    /// نافذة واحدة لعمليات الحساب التلاتة — الخانات بتظهر وتختفي حسب
    /// العملية. تلات نوافذ متشابهة كانت هتخلي أي تعديل في الشكل أو في
    /// قواعد التحقق يتعمل تلات مرات.
    ///
    /// النافذة بتجمّع المدخلات وبتتحقق من الشكل بس (خانة فاضية، كلمتين
    /// مش متطابقين). التحقق الحقيقي — الاسم متاح؟ كلمة المرور صح؟ — في
    /// <see cref="AuthService"/>، عشان القاعدة تتطبق من أي مسار.
    /// </summary>
    public partial class AccountActionDialog : Window
    {
        private readonly AccountAction _action;

        private AccountActionDialog(AccountAction action, string currentUsername)
        {
            InitializeComponent();
            _action = action;

            Title = TitleFor(action);
            TitleText.Text = Title;

            CurrentPasswordLabel.Text = "كلمة مرور الدخول الحالية";
            CurrentPasswordHint.Text = action == AccountAction.AddAccount
                ? $"كلمة مرور حسابك انت (\"{currentUsername}\") — عشان محدش يضيف حساب من جهاز مسيّب مفتوح."
                : $"كلمة مرور حسابك الحالية (\"{currentUsername}\").";

            switch (action)
            {
                case AccountAction.ChangeUsername:
                    SubtitleText.Text = "الاسم اللي بتسجّل بيه الدخول";
                    UsernameLabel.Text = "اسم المستخدم الجديد";
                    UsernameBox.Text = currentUsername;
                    NewPasswordSection.Visibility = Visibility.Collapsed;
                    break;

                case AccountAction.ChangeLoginPassword:
                    SubtitleText.Text = "كلمة المرور اللي بتفتح بيها البرنامج";
                    UsernameSection.Visibility = Visibility.Collapsed;
                    NewPasswordLabel.Text = "كلمة المرور الجديدة (ومرة تانية للتأكيد)";
                    break;

                case AccountAction.AddAccount:
                    SubtitleText.Text = "حساب دخول تاني بنفس الصلاحيات بالظبط";
                    UsernameLabel.Text = "اسم المستخدم الجديد";
                    NewPasswordLabel.Text = "كلمة مروره (ومرة تانية للتأكيد)";
                    break;
            }

            ConfirmButton.Content = action == AccountAction.AddAccount ? "إضافة" : "حفظ";
            Loaded += (_, _) => FirstBox(action).Focus();
        }

        // ------- المخرجات -------

        public string EnteredUsername => UsernameBox.Text.Trim();
        public string EnteredNewPassword => NewPasswordBox.Password;
        public string EnteredCurrentPassword => CurrentPasswordBox.Password;

        /// <summary>بيعرض النافذة ويرجّع true لو المستخدم أكّد</summary>
        public static AccountActionDialog? Ask(
            Window? owner, AccountAction action, string currentUsername)
        {
            var dialog = new AccountActionDialog(action, currentUsername);
            if (owner is not null) dialog.Owner = owner;

            return dialog.ShowDialog() == true ? dialog : null;
        }

        private static string TitleFor(AccountAction action) => action switch
        {
            AccountAction.ChangeUsername => "تغيير اسم المستخدم",
            AccountAction.ChangeLoginPassword => "تغيير كلمة مرور الدخول",
            _ => "إضافة حساب دخول"
        };

        private System.Windows.Controls.Control FirstBox(AccountAction action) =>
            action == AccountAction.ChangeLoginPassword ? NewPasswordBox : UsernameBox;

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var needsUsername = _action != AccountAction.ChangeLoginPassword;
            var needsNewPassword = _action != AccountAction.ChangeUsername;

            if (needsUsername && EnteredUsername.Length < AuthService.MinUsernameLength)
            {
                Fail($"اسم المستخدم لازم يكون {AuthService.MinUsernameLength} حروف على الأقل", UsernameBox);
                return;
            }

            if (needsNewPassword)
            {
                if (EnteredNewPassword.Length < AuthService.MinPasswordLength)
                {
                    Fail($"كلمة المرور لازم تكون {AuthService.MinPasswordLength} حروف/أرقام على الأقل",
                        NewPasswordBox);
                    return;
                }

                // التأكيد بيمنع الغلطة اللي بتقفل المستخدم بره حسابه:
                // كلمة اتكتبت غلط مرة واحدة ومحدش يعرف هي إيه
                if (EnteredNewPassword != ConfirmPasswordBox.Password)
                {
                    Fail("الكلمتين مش متطابقين", ConfirmPasswordBox);
                    return;
                }
            }

            if (EnteredCurrentPassword.Length == 0)
            {
                Fail("اكتب كلمة مرورك الحالية", CurrentPasswordBox);
                return;
            }

            DialogResult = true;
        }

        private void Fail(string message, System.Windows.Controls.Control focus)
        {
            ErrorBox.ShowError(ErrorText, message);
            focus.Focus();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
