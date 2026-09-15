using System.Windows;
using System.Windows.Input;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تفاصيل منتج واحد — بتفتح Modal لما تدوس على كارت في شبكة
    /// المنتجات (شوف ProductsView.xaml.cs's ProductTile_Click). الـ
    /// DataContext هو نفس ProductsViewModel المشترك مع الشاشة الأصلية، مش
    /// نسخة تانية — كل الأوامر (تعديل، حذف، ترتيب المراحل...) شغالة زي ما
    /// هي من غير أي تغيير في الـ ViewModel نفسه.
    /// </summary>
    public partial class ProductDetailDialog : Window
    {
        public ProductDetailDialog(ProductsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
