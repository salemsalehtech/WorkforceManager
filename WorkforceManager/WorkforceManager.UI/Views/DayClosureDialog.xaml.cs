using System.Windows;
using System.Windows.Input;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// مراجعة إقفال اليوم: بتعرض اللي خلص الخط واللي دخله النهارده قبل ما
    /// المستخدم يوافق. الكود هنا شكلي بس — الإقفال نفسه في DayClosureService.
    /// </summary>
    public partial class DayClosureDialog : Window
    {
        public DayClosureDialog(DayClosurePreviewDto preview)
        {
            InitializeComponent();

            DateText.Text = preview.Date.ToString("dddd yyyy/MM/dd");
            CompletedText.Text = preview.CompletedPieces.ToString("N0");
            CarriedText.Text = preview.StartedPieces.ToString("N0");

            // الهالك بيختفي خالص لما ميكونش موجود بدل ما يعرض صفر —
            // صفر هالك مش معلومة، وبياخد مساحة من الأرقام اللي بتتقري
            ScrapCard.Visibility = preview.HasScrap ? Visibility.Visible : Visibility.Collapsed;
            ScrapText.Text = preview.ScrapPieces.ToString("N0");

            LotsList.ItemsSource = preview.ByProduct;

            var hasActivity = preview.ByProduct.Count > 0;
            CarriedHeader.Visibility = hasActivity ? Visibility.Visible : Visibility.Collapsed;
            LotsScroller.Visibility = hasActivity ? Visibility.Visible : Visibility.Collapsed;
            NothingCarried.Visibility = hasActivity ? Visibility.Collapsed : Visibility.Visible;
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
