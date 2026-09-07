using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// ديالوج حاجز عند بدء التشغيل للأيام اللي البرنامج قفل قبل ما
    /// يتوقّع عليها. مفيش طريقة تعدّي من غيره — لا زرار إلغاء ولا X
    /// شغّال (شوف Window_Closing) — عشان اللحاق ده شرط أمان مش تنظيف.
    /// </summary>
    public partial class LateSignOffCatchUpDialog : Window
    {
        private readonly Func<string, Task> _onConfirm;
        private bool _acknowledged;

        /// <param name="dates">الأيام اللي محتاجة إقرار، الأقدم الأول</param>
        /// <param name="passwordRequired">فيه كلمة سر عمليات متسجّلة أصلًا؟</param>
        /// <param name="onConfirm">
        /// بينفّذ الإقرار الفعلي (AcknowledgeLateAsync) — بيرمي استثناء
        /// برسالة عربية لو الباسورد غلط، والديالوج بيعرضها ويسيب المستخدم
        /// يجرّب تاني من غير ما يقفل.
        /// </param>
        public LateSignOffCatchUpDialog(
            IReadOnlyList<DateTime> dates, bool passwordRequired, Func<string, Task> onConfirm)
        {
            InitializeComponent();
            _onConfirm = onConfirm;

            DatesList.ItemsSource = dates
                .OrderBy(d => d)
                .Select(d => d.ToString("dddd yyyy/MM/dd"))
                .ToList();

            PasswordSection.Visibility = passwordRequired ? Visibility.Visible : Visibility.Collapsed;
            NotConfiguredBox.Visibility = passwordRequired ? Visibility.Collapsed : Visibility.Visible;

            Closing += Window_Closing;
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            // مفيش طريق يخرج من غير إقرار — لا X ولا Alt+F4
            if (!_acknowledged) e.Cancel = true;
        }

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            ConfirmButton.IsEnabled = false;
            try
            {
                await _onConfirm(PasswordBox.Password);
                _acknowledged = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorBox.ShowError(ErrorText, ex.Message);
            }
            finally
            {
                ConfirmButton.IsEnabled = true;
            }
        }
    }
}
