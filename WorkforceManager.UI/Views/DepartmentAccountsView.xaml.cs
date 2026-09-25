using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة الحسابات الإدارية: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في DepartmentAccountsViewModel حسب نمط MVVM المتبع.
    /// </summary>
    public partial class DepartmentAccountsView : UserControl, IScreenShortcuts
    {
        public bool TryQuickAdd() =>
            KeyboardShortcuts.TryExecute((DataContext as DepartmentAccountsViewModel)?.AddAccountCommand);

        public DepartmentAccountsView(DepartmentAccountsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += (_, _) => EntranceAnimation.PlayFadeSlideIn(this);
            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }
    }
}
