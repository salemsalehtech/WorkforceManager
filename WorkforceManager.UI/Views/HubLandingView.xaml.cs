using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>صفحة كروت — الكود هنا بس بيملّي القايمة وبيرفع حدث الدوسة، شوف HubLandingView.xaml</summary>
    public partial class HubLandingView : UserControl
    {
        /// <summary>الدوسة على كارت — Key بتاعه هو اللي الأب بيفتح بيه الصفحة</summary>
        public event Action<HubCardInfo>? CardChosen;

        public HubLandingView() => InitializeComponent();

        public void Setup(string title, string subtitle, IReadOnlyList<HubCardInfo> cards)
        {
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            CardsList.ItemsSource = cards;
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: HubCardInfo card }) CardChosen?.Invoke(card);
        }
    }
}
