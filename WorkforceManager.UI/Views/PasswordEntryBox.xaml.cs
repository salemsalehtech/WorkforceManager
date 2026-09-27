using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// PasswordBox زائد زرار "عين" لإظهار/إخفاء كلمة المرور — شوف تعليق
    /// XAML للسبب في وجود صندوقين (PasswordBox وTextBox) بدل واحد.
    /// كل استخدامات PasswordBox القديمة في الديالوجات بتقرأ .Password
    /// بس عادةً، فده هو أهم عضو هنا — الباقي (Focus/Clear) بديل مباشر
    /// لنفس أعضاء PasswordBox اللي الكود القديم كان بيستخدمها.
    /// </summary>
    public partial class PasswordEntryBox : UserControl
    {
        private bool _syncing;

        public PasswordEntryBox()
        {
            InitializeComponent();
        }

        /// <summary>القيمة الحقيقية — المصدر دايمًا HiddenBox، اتزامن VisibleBox معاه أو لأ</summary>
        public string Password => HiddenBox.Password;

        /// <summary>بديل PasswordBox.Focus() — بيركّز على أي صندوق ظاهر دلوقتي فعليًا</summary>
        public new bool Focus() => VisibleBox.Visibility == Visibility.Visible ? VisibleBox.Focus() : HiddenBox.Focus();

        /// <summary>بديل PasswordBox.Clear() — بيفضّي الصندوقين مع بعض</summary>
        public void Clear()
        {
            HiddenBox.Clear();
            VisibleBox.Clear();
        }

        private void HiddenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            VisibleBox.Text = HiddenBox.Password;
            _syncing = false;
        }

        private void VisibleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            HiddenBox.Password = VisibleBox.Text;
            _syncing = false;
        }

        private void ToggleVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            var showing = VisibleBox.Visibility == Visibility.Visible;

            VisibleBox.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
            HiddenBox.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;

            EyeIcon.Kind = showing ? PackIconKind.EyeOutline : PackIconKind.EyeOffOutline;
            var tooltip = showing ? "إظهار كلمة المرور" : "إخفاء كلمة المرور";
            ToggleVisibilityButton.ToolTip = tooltip;
            AutomationProperties.SetName(ToggleVisibilityButton, tooltip);

            // المؤشر يفضل في آخر النص وقت التبديل، مش يرجع الأول
            if (!showing) VisibleBox.CaretIndex = VisibleBox.Text.Length;
            Focus();
        }
    }
}
