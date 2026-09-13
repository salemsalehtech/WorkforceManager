using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkforceManager.Core.Models;

namespace WorkforceManager.UI.Views
{
    /// <summary>مرحلة معروضة في قايمة اختيار مرحلة الهالك</summary>
    public record ScrapStageChoice(int StageId, string Display, int AvailablePieces);

    /// <summary>منتج ومراحله — الخطوة الأولى في اختيار الهالك (منتج فقبله مرحلة)</summary>
    public record ScrapProductChoice(int ProductId, string Name, IReadOnlyList<ScrapStageChoice> Stages);

    /// <summary>
    /// تسجيل هالك: قطع اتشالت من الخط ومش هتتكمّل.
    ///
    /// المستخدم بيفتحها بنفسه من تبويب الهالك (مثلاً الجودة رفضت منتج
    /// خلص الخط كله) — مفيش سؤال تلقائي بعد الحفظ (اتشال، شوف
    /// FlowSessionViewModel)، فمفيش افتراض إن أي فرق "هيتكمّل بكرة"؛
    /// القطع الواقفة تفضل واقفة لحد ما المستخدم يقرر بنفسه هو ولا هالك.
    ///
    /// الكود هنا شكلي بس — القاعدة والحساب في ScrapService.
    /// </summary>
    public partial class ScrapDialog : Window
    {
        private int _maxPieces;

        private ScrapDialog(IReadOnlyList<ScrapProductChoice> products, IReadOnlyList<ScrapReason> reasons)
        {
            InitializeComponent();

            ProductBox.ItemsSource = products;
            ReasonBox.ItemsSource = reasons;
            if (reasons.Count > 0) ReasonBox.SelectedIndex = 0;
        }

        /// <summary>عدد قطع الهالك اللي المستخدم أكّده</summary>
        public int PieceCount { get; private set; }

        public int StageId { get; private set; }

        public int? ReasonId { get; private set; }

        public string? Note { get; private set; }

        public static ScrapDialog ForStage(
            Window owner,
            IReadOnlyList<ScrapProductChoice> products,
            IReadOnlyList<ScrapReason> reasons)
        {
            var dialog = new ScrapDialog(products, reasons) { Owner = owner };

            dialog.SubtitleText.Text = "قطع اتشالت من الخط";

            // بيختار أول منتج، وده بيسلسل Product_Changed فيملّي StageBox
            // بمراحله تلقائيًا — مفيش لازمة لتحديد المرحلة يدوي هنا
            if (products.Count > 0) dialog.ProductBox.SelectedIndex = 0;

            return dialog;
        }

        /// <summary>
        /// اختيار المنتج بيملّي StageBox بمراحله بس — نفس بيانات الاختيار
        /// الأول (ScrapProductChoice.Stages)، مفيش استعلام تاني.
        /// </summary>
        private void Product_Changed(object sender, SelectionChangedEventArgs e)
        {
            var stages = (ProductBox.SelectedItem as ScrapProductChoice)?.Stages;
            StageBox.ItemsSource = stages;
            StageBox.SelectedIndex = stages is { Count: > 0 } ? 0 : -1;
        }

        private void Stage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (StageBox.SelectedItem is ScrapStageChoice choice)
                _maxPieces = choice.AvailablePieces;

            UpdateRemainder();
        }

        private void Pieces_Changed(object sender, TextChangedEventArgs e) => UpdateRemainder();

        /// <summary>
        /// بيعرض الباقي بعد الهالك وانت بتكتب — عشان تشوف نتيجة الرقم
        /// قبل ما تحفظ مش بعده.
        /// </summary>
        private void UpdateRemainder()
        {
            if (RemainderText is null) return;

            if (!int.TryParse(PiecesBox.Text?.Trim(), out var pieces) || pieces <= 0)
            {
                RemainderText.Text = "";
                return;
            }

            var remainder = _maxPieces - pieces;

            RemainderText.Text = remainder switch
            {
                > 0 => $"الباقي {remainder:N0} قطعة هيفضلوا واقفين مستنيين يتكمّلوا.",
                0 when _maxPieces > 0 => "كل الفرق هيتسجّل هالك — مفيش حاجة هتفضل واقفة.",
                _ => ""
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            if (ProductBox.SelectedItem is not ScrapProductChoice)
            {
                ShowError("اختار المنتج الأول");
                return;
            }

            if (StageBox.SelectedItem is not ScrapStageChoice stage)
            {
                ShowError("اختار المرحلة");
                return;
            }

            if (!int.TryParse(PiecesBox.Text?.Trim(), out var pieces) || pieces <= 0)
            {
                ShowError("اكتب عدد قطع أكبر من صفر");
                return;
            }

            // الحد الأقصى من الفرق نفسه: هالك أكتر من اللي خلص المرحلة
            // معناه رقم غلط، ولو عدّى هيطلع شغل واقف بالسالب
            if (_maxPieces > 0 && pieces > _maxPieces)
            {
                ShowError($"العدد أكبر من المتاح ({_maxPieces:N0} قطعة)");
                return;
            }

            StageId = stage.StageId;
            PieceCount = pieces;
            ReasonId = (ReasonBox.SelectedItem as ScrapReason)?.Id;
            Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
