using System.Windows;
using System.Windows.Input;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// نافذة تفاصيل عامل واحد — بتفتح Modal لما تدوس على كارت في شبكة
    /// العمال (شوف WorkersView.xaml.cs's WorkerTile_Click). الـ DataContext
    /// هو نفس WorkersViewModel المشترك مع الشاشة الأصلية، مش نسخة تانية —
    /// كل الأوامر (تعديل، حذف، مهارات، هستوري...) شغالة زي ما هي من غير أي
    /// تغيير في الـ ViewModel نفسه.
    /// </summary>
    public partial class WorkerDetailDialog : ChromelessDialogWindow
    {
        public WorkerDetailDialog(WorkersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
