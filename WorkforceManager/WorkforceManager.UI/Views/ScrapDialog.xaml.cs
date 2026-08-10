using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkforceManager.Core.Models;

namespace WorkforceManager.UI.Views
{
    /// <summary>مرحلة معروضة في قايمة اختيار مرحلة الهالك</summary>
    public record ScrapStageChoice(int StageId, string Display, int AvailablePieces);

    /// <summary>
    /// تسجيل هالك: قطع اتشالت من الخط ومش هتتكمّل.
    ///
    /// بتتفتح بشكلين:
    ///   • <see cref="ForGap"/> — البرنامج لقى فرق بين مرحلتين بعد
    ///     الحفظ وبيسأل عنه. المرحلة متحددة ومقفولة، والعدد مملّي
    ///     بالفرق كله.
    ///   • <see cref="ForStage"/> — المستخدم فتحها بنفسه من تبويب
    ///     الهالك (مثلاً الجودة رفضت منتج خلص الخط كله).
    ///
    /// الكود هنا شكلي بس — القاعدة والحساب في ScrapService.
    /// </summary>
    public partial class ScrapDialog : Window
    {
        private int _maxPieces;

        private ScrapDialog(IReadOnlyList<ScrapStageChoice> stages, IReadOnlyList<ScrapReason> reasons)
        {
            InitializeComponent();

            StageBox.ItemsSource = stages;
            ReasonBox.ItemsSource = reasons;
            if (reasons.Count > 0) ReasonBox.SelectedIndex = 0;
        }

        /// <summary>عدد قطع الهالك اللي المستخدم أكّده</summary>
        public int PieceCount { get; private set; }

        public int StageId { get; private set; }

        public int? ReasonId { get; private set; }

        public string? Note { get; private set; }

        /// <summary>
        /// البرنامج لقى فرق بين مرحلتين — بيسأل عنه بدل ما يفترض إنه
        /// هيتكمّل بكرة ويسيبه واقف للأبد.
        /// </summary>
        public static ScrapDialog ForGap(
            Window owner,
            string productName,
            string previousStageName,
            string currentStageName,
            int previousStageId,
            int gapPieces,
            IReadOnlyList<ScrapReason> reasons)
        {
            var stage = new ScrapStageChoice(
                previousStageId, $"{productName} — {previousStageName}", gapPieces);

            var dialog = new ScrapDialog(new[] { stage }, reasons)
            {
                Owner = owner,
                _maxPieces = gapPieces
            };

            dialog.SubtitleText.Text = productName;
            dialog.StageBox.SelectedIndex = 0;
            dialog.StageBox.IsEnabled = false; // المرحلة معروفة من الفرق نفسه

            dialog.GapHeadline.Text =
                $"فيه {gapPieces:N0} قطعة خلصت \"{previousStageName}\" وماكملتش لـ\"{currentStageName}\"";
            dialog.GapDetail.Text =
                "لو دول هالك مش هيتكمّلوا، سجّلهم هنا عشان يتشالوا من الشغل الواقف. " +
                "ولو هيتكمّلوا بكرة، اقفل النافذة وسيبهم زي ما هم.";

            dialog.PiecesBox.Text = gapPieces.ToString();
            dialog.UpdateRemainder();

            return dialog;
        }

        /// <summary>
        /// المستخدم بيسجّل هالك بنفسه — لأي مرحلة، وأهمها **آخر مرحلة**:
        /// دي الحالة اللي مفيش فرق بين المراحل بيكشفها، لأن مفيش مرحلة
        /// بعد الأخيرة نقارنها بيها.
        /// </summary>
        public static ScrapDialog ForStage(
            Window owner,
            IReadOnlyList<ScrapStageChoice> stages,
            IReadOnlyList<ScrapReason> reasons)
        {
            var dialog = new ScrapDialog(stages, reasons) { Owner = owner };

            dialog.SubtitleText.Text = "قطع اتشالت من الخط";
            dialog.GapCard.Visibility = Visibility.Collapsed;
            dialog.KeepPendingButton.Content = "إلغاء";

            if (stages.Count > 0) dialog.StageBox.SelectedIndex = 0;

            return dialog;
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

            if (StageBox.SelectedItem is not ScrapStageChoice stage)
            {
                ShowError("اختار المرحلة الأول");
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
