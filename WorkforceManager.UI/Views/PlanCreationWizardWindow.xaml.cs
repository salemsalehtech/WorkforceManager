using System.Windows;
using System.Windows.Input;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// wizard إنشاء خطة جديدة للشهر — DialogResult=true بس لو الحفظ نجح
    /// فعلاً (PlanCreationWizardViewModel.Saved)، عشان الشاشة اللي فتحته
    /// تعرف تعيد التحميل من عدمه.
    /// </summary>
    public partial class PlanCreationWizardWindow : Window
    {
        private readonly PlanCreationWizardViewModel _viewModel;

        public PlanCreationWizardWindow(PlanCreationWizardViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            if (viewModel.IsEditMode)
                Loaded += async (_, _) => await _viewModel.LoadExistingPlanAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var saved = await _viewModel.SaveAsync();
            if (saved)
            {
                DialogResult = true;
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = _viewModel.Saved;

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
