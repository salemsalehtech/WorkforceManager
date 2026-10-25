using System.Windows;
using System.Windows.Controls;

namespace WorkforceManager.UI.Views
{
    /// <summary>شريط الرجوع/المسار فوق صفحة جوه نظام الكروت — شوف HubPageBar.xaml</summary>
    public partial class HubPageBar : UserControl
    {
        public event Action? BackRequested;

        public HubPageBar() => InitializeComponent();

        /// <summary>
        /// المسار في سطر واحد: الشاشة ← الصفحة (← الصفحة الداخلية لو الصفحة نفسها ليها كروت، زي التسجيل اليومي).
        /// </summary>
        public void Set(string hubTitle, string pageTitle, string? subPageTitle = null)
        {
            HubText.Text = hubTitle;
            PageText.Text = pageTitle;

            var hasSub = !string.IsNullOrEmpty(subPageTitle);
            PageText.FontWeight = hasSub ? FontWeights.Normal : FontWeights.Bold;
            PageText.Foreground = (System.Windows.Media.Brush)FindResource(hasSub ? "TextMutedBrush" : "TextPrimaryBrush");
            SubChevron.Visibility = SubText.Visibility = hasSub ? Visibility.Visible : Visibility.Collapsed;
            SubText.Text = subPageTitle ?? "";
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
    }
}
