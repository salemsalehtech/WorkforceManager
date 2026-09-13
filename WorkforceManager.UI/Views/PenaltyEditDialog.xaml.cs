using System.Windows;
using System.Windows.Input;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// تعديل سبب الجزاء وخصمه.
    ///
    /// بتجمع المدخلات بس — التحقق إن الجزاء يدوي (مش تلقائي) وكلمة السر
    /// في <c>PenaltyService</c>، عشان القاعدة تتطبق من أي مسار مش من
    /// الشاشة دي بس.
    /// </summary>
    public partial class PenaltyEditDialog : Window
    {
        public PenaltyEditDialog(string workerName, string reason, string deductionName)
        {
            InitializeComponent();

            WorkerText.Text = workerName;
            ReasonBox.Text = reason;

            var options = Enum.GetValues<PenaltyDeduction>()
                .Select(value => new DeductionOption(value))
                .ToList();

            DeductionBox.ItemsSource = options;
            // الخصم الحالي بيتحدد بالاسم المعروض — الصف اللي في القايمة
            // شايل النص مش القيمة
            DeductionBox.SelectedItem =
                options.FirstOrDefault(o => o.Display == deductionName) ?? options[0];

            Loaded += (_, _) => ReasonBox.Focus();
        }

        public string PenaltyReason => ReasonBox.Text.Trim();

        public PenaltyDeduction Deduction =>
            ((DeductionOption)DeductionBox.SelectedItem).Value;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (PenaltyReason.Length == 0)
            {
                ErrorText.ShowError("سبب الجزاء مطلوب");
                ReasonBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
