using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// مراجعة إقفال اليوم: بتعرض اللي خلص واللي لسه مستني في الخط قبل ما
    /// المستخدم يوافق. الكود هنا شكلي بس — الإقفال نفسه في DayClosureService.
    /// </summary>
    public partial class DayClosureDialog : Window
    {
        public DayClosureDialog(DayClosurePreviewDto preview)
        {
            InitializeComponent();

            DateText.Text = preview.Date.ToString("dddd yyyy/MM/dd");
            CompletedText.Text = preview.CompletedPieces.ToString("N0");
            CarriedText.Text = preview.ParkedPieces.ToString("N0");

            LotsList.ItemsSource = preview.ParkedByProduct;

            var hasParked = preview.ParkedByProduct.Count > 0;
            CarriedHeader.Visibility = hasParked ? Visibility.Visible : Visibility.Collapsed;
            LotsScroller.Visibility = hasParked ? Visibility.Visible : Visibility.Collapsed;
            NothingCarried.Visibility = hasParked ? Visibility.Collapsed : Visibility.Visible;

            OverCountWarning.Visibility = preview.HasOverCounting ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
