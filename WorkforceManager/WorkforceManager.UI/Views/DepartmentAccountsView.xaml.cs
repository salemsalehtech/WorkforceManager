using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة الحسابات الإدارية: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في DepartmentAccountsViewModel حسب نمط MVVM المتبع.
    /// </summary>
    public partial class DepartmentAccountsView : UserControl
    {
        public DepartmentAccountsView(DepartmentAccountsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }
    }
}
