using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة العمال: الكود هنا شكلي بس (ربط الـ ViewModel) — كل المنطق
    /// في WorkersViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class WorkersView : UserControl
    {
        public WorkersView(WorkersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // تحميل البيانات أول ما الشاشة تظهر (مش في الـ Constructor عشان الواجهة متعلقش)
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }
    }
}
