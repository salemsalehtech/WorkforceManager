using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الذاكرة" — خطط الإنتاج المتأجّلة.
    ///
    /// الكود هنا بيعمل حاجة واحدة بس: يفتح نافذة الترتيب. الدايالوج
    /// محتاج نافذة أب (Owner) والـ ViewModel مالوش وصول لنوافذ — فالشاشة
    /// بتجيب المدخلات منه، تعرض النافذة، وترجّعله النتيجة.
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
    }
}
