using System.Windows;
using System.Windows.Controls;
using WorkforceManager.Business.DTOs;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الذاكرة" — خطط الإنتاج المتأجّلة.
    ///
    /// الكود هنا بيعمل حاجتين محتاجين نافذة أب (Owner) والـ ViewModel
    /// مالوش وصول لنوافذ: ترتيب المراحل، وتأجيل سريع من الكارت. الشاشة
    /// بتجيب المدخلات من الـ ViewModel، تعرض النافذة، وترجّعله النتيجة.
    /// </summary>
    public partial class MemoryView : UserControl
    {
        private readonly MemoryViewModel _viewModel;

        public MemoryView(MemoryViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += async (_, _) => await viewModel.LoadAsync();
        }

        private void ReorderStages_Click(object sender, RoutedEventArgs e)
        {
            var stages = _viewModel.StagesForOrdering();
            if (stages.Count == 0) return;

            var chosen = MemoryStageOrderDialog.Ask(
                Window.GetWindow(this), stages, _viewModel.StageOrder);

            // null = المستخدم لغى، فالترتيب القديم يفضل زي ما هو
            if (chosen is not null) _viewModel.ApplyStageOrder(chosen);
        }

        private async void Postpone_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not ProductionMemoryDto memory) return;

            var picked = MemoryPostponeDialog.Ask(Window.GetWindow(this), memory.RemindOn);
            if (picked is null) return; // لغى اختيار اليوم

            await _viewModel.PostponeAsync(memory.Id, picked.Value);
        }
    }
}
