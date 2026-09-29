using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>شاشة "الإحصائيات" — عرض بس، مفيش إدخال هنا خالص</summary>
    public partial class StatisticsView : UserControl
    {
        private readonly StatisticsViewModel _viewModel;
        private readonly TaskCompletionSource _loadedTcs = new();

        public Task WhenLoaded => _loadedTcs.Task;

        public StatisticsView(StatisticsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += (_, _) => EntranceAnimation.PlayFadeSlideIn(this);
            Loaded += async (_, _) =>
            {
                await viewModel.LoadAsync();
                _loadedTcs.TrySetResult();
            };
        }
    }
}
