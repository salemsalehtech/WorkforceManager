using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>شاشة "الدليل" — مرجع دائم لكل شاشات البرنامج، شوف HelpViewModel</summary>
    public partial class HelpView : UserControl
    {
        public HelpView(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
