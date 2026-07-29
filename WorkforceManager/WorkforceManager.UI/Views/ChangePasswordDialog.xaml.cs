using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تغيير كلمة المرور — بتتطلب كلمة المرور الحالية،
    /// وقواعد التحقق كلها في AuthService.
    /// </summary>
    public partial class ChangePasswordDialog : Window
    {
        public ChangePasswordDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (UsernameBox.Text.Length > 0) CurrentBox.Focus();
                else UsernameBox.Focus();
            };
        }

        /// <summary>تعبئة اسم المستخدم من شاشة الدخول توفيرًا للكتابة</summary>
        public void PrefillUsername(string username) => UsernameBox.Text = username;

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            if (NewBox.Password != ConfirmBox.Password)
            {
                ShowError("كلمة المرور الجديدة والتأكيد مش متطابقين");
                ConfirmBox.Clear();
                ConfirmBox.Focus();
                return;
            }

            try
            {
                using var scope = App.AppHost.Services.CreateScope();
                var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
                await auth.ChangePasswordAsync(UsernameBox.Text.Trim(), CurrentBox.Password, NewBox.Password);

                MessageBox.Show("تم تغيير كلمة المرور بنجاح", "تم",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (System.InvalidOperationException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>النافذة بلا إطار نظام — السحب من الشريط العلوي</summary>
        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
