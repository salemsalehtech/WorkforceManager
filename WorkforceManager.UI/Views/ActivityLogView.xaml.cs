using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة سجل العمليات: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في ActivityLogViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class ActivityLogView : UserControl
    {
        public ActivityLogView(ActivityLogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += async (_, _) => await viewModel.LoadAsync();
        }
    }
}
