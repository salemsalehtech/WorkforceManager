using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// مراجعة إقفال اليوم: بتعرض اللي خلص واللي هيترحّل لبكرة قبل ما
    /// المستخدم يوافق. الكود هنا شكلي بس — الإقفال نفسه في DayClosureService.
    /// </summary>
    public partial class DayClosureDialog : Window
    {
        public DayClosureDialog(DayClosurePreviewDto preview)
        {
            InitializeComponent();

            DateText.Text = preview.Date.ToString("dddd yyyy/MM/dd");
            CompletedText.Text = preview.CompletedPieces.ToString("N0");
            CarriedText.Text = preview.CarriedPieces.ToString("N0");

            LotsList.ItemsSource = preview.CarriedLots;

            var hasCarried = preview.CarriedLots.Count > 0;
            CarriedHeader.Visibility = hasCarried ? Visibility.Visible : Visibility.Collapsed;
            LotsScroller.Visibility = hasCarried ? Visibility.Visible : Visibility.Collapsed;
            NothingCarried.Visibility = hasCarried ? Visibility.Collapsed : Visibility.Visible;
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
