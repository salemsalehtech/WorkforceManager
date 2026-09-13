using System.Linq;
using System.Windows;
using System.Windows.Input;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تصحيح سجل إنتاج محفوظ: عدد القطع، واختياريًا نقل السجل
    /// بالكامل لعامل تاني (اتسجّل على عامل غلط بالغلط). التحقق هنا شكلي —
    /// التعديل الفعلي ونقل اليومية بين العمال مسؤولية
    /// WorkdayCalculationService.UpdateProductionAsync.
    /// </summary>
    public partial class EditProductionDialog : Window
    {
        private int _originalWorkerId;

        public EditProductionDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => { PiecesBox.Focus(); PiecesBox.SelectAll(); };
        }

        /// <summary>عدد القطع الجديد (مضمون رقم موجب بعد Save_Click)</summary>
        public int NewPieceCount => int.Parse(PiecesBox.Text.Trim());

        /// <summary>العامل المختار حاليًا في القائمة (ممكن يكون نفس عامل السجل الأصلي)</summary>
        public WorkerPick SelectedWorker => (WorkerPick)WorkerBox.SelectedItem;

        /// <summary>هل المستخدم فعليًا غيّر العامل عن اللي كان مسجّل بيه السجل؟</summary>
        public bool WorkerChanged => SelectedWorker.WorkerId != _originalWorkerId;

        /// <summary>
        /// تعبئة بيانات السجل المعروضة والقيمة الحالية، وقائمة العمال
        /// المؤهلين على نفس المرحلة. العامل الحالي بيتحط في القائمة
        /// دايمًا حتى لو مش من ضمن المؤهلين رسميًا (مهارة اتشالت بعد
        /// التسجيل مثلًا) — عشان القائمة تقدر تعرض اختياره الحالي.
        /// </summary>
        public void LoadRecord(
            int workerId, string workerName, string stageDisplay, int currentPieces,
            IReadOnlyList<WorkerPick> workerOptions)
        {
            _originalWorkerId = workerId;
            StageText.Text = stageDisplay;
            PiecesBox.Text = currentPieces.ToString();

            var options = workerOptions.Any(w => w.WorkerId == workerId)
                ? workerOptions
                : workerOptions.Prepend(new WorkerPick(workerId, workerName)).ToList();

            WorkerBox.ItemsSource = options;
            WorkerBox.SelectedItem = options.First(w => w.WorkerId == workerId);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PiecesBox.Text.Trim(), out var pieces) || pieces <= 0)
            {
                ErrorText.ShowError("عدد القطع لازم يكون رقم صحيح موجب");
                PiecesBox.Focus();
                return;
            }

            if (WorkerBox.SelectedItem is null)
            {
                ErrorText.ShowError("اختار العامل");
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
