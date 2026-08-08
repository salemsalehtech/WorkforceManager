using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة تسجيل الدخول الترحيبية — أول حاجة بتظهر عند فتح البرنامج.
    /// التحقق الفعلي من البيانات مسؤولية AuthService (تشفير PBKDF2)،
    /// الشاشة هنا بس بتجمع المدخلات وتعرض النتيجة.
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UsernameBox.Focus();
        }

        /// <summary>المستخدم اللي سجل دخول بنجاح (بيقرأه App بعد إغلاق الشاشة)</summary>
        public string? LoggedInDisplayName { get; private set; }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.ClearError();

            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (username.Length == 0 || password.Length == 0)
            {
                ErrorText.ShowError("اكتب اسم المستخدم وكلمة المرور الأول");
                return;
            }

            using var scope = App.AppHost.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var user = await auth.ValidateLoginAsync(username, password);
            if (user is null)
            {
                // رسالة واحدة للحالتين عمدًا — مش بنقول للمتطفل أنهي جزء الغلط
                ErrorText.ShowError("اسم المستخدم أو كلمة المرور غير صحيحة");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            LoggedInDisplayName = user.DisplayName ?? user.Username;

            // الهوية المشتركة: من هنا ورايح كل حذف وكل حدث في السجل
            // بياخد اسم الشخص ده. Singleton فبيتقري من أي Scope بعدين.
            App.AppHost.Services.GetRequiredService<CurrentUserContext>()
                .SignIn(user.Username, user.DisplayName);

            DialogResult = true;
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChangePasswordDialog { Owner = this };
            // تعبئة اسم المستخدم المكتوب بالفعل توفيرًا للكتابة
            dialog.PrefillUsername(UsernameBox.Text.Trim());
            dialog.ShowDialog();
        }


        /// <summary>النافذة بلا إطار نظام — السحب من أي مكان فاضي فيها</summary>
        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // إغلاق شاشة الدخول = إغلاق البرنامج (بيتم في App.OnStartup)
            DialogResult = false;
        }
    }
}
