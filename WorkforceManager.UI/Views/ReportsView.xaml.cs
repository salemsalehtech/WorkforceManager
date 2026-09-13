using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة التقارير والتقييم: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في ReportsViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class ReportsView : UserControl
    {
        public ReportsView(ReportsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // تحميل تقرير اليوم وكشف الأسبوع أول ما الشاشة تظهر
            Loaded += async (_, _) => await viewModel.InitializeAsync();
        }
    }
}
