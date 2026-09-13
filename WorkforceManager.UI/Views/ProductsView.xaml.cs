using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة المنتجات والمراحل: الكود هنا شكلي بس (ربط الـ ViewModel) —
    /// كل المنطق في ProductsViewModel حسب نمط MVVM المتبع في المشروع.
    /// </summary>
    public partial class ProductsView : UserControl
    {
        public ProductsView(ProductsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // تحميل المنتجات أول ما الشاشة تظهر
            Loaded += async (_, _) => await viewModel.LoadAsync();
        }
    }
}
