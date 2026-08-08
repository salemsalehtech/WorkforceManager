using System.Windows;
using System.Windows.Input;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تصحيح عدد قطع سجل إنتاج محفوظ. التحقق هنا شكلي (رقم موجب) —
    /// التعديل الفعلي مسؤولية WorkdayCalculationService.UpdateProductionAsync.
    /// </summary>
    public partial class EditProductionDialog : Window
    {
        public EditProductionDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => { PiecesBox.Focus(); PiecesBox.SelectAll(); };
        }

        /// <summary>عدد القطع الجديد (مضمون رقم موجب بعد Save_Click)</summary>
        public int NewPieceCount => int.Parse(PiecesBox.Text.Trim());

        /// <summary>تعبئة بيانات السجل المعروضة والقيمة الحالية</summary>
        public void LoadRecord(string workerName, string stageDisplay, int currentPieces)
        {
            WorkerText.Text = workerName;
            StageText.Text = stageDisplay;
            PiecesBox.Text = currentPieces.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PiecesBox.Text.Trim(), out var pieces) || pieces <= 0)
            {
                ErrorText.ShowError("عدد القطع لازم يكون رقم صحيح موجب");
                PiecesBox.Focus();
                return;
            }

            DialogResult = true;
        }

        /// <summary>النافذة بلا إطار نظام — السحب من الشريط العلوي</summary>
        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
