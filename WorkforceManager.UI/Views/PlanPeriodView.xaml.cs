using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "خطة بفترة مخصصة" — الحفظ عند فقدان تركيز خانة الكمية، نفس
    /// نمط MonthlyPlanView بالظبط.
    /// </summary>
    public partial class PlanPeriodView : UserControl
    {
        private readonly PlanPeriodViewModel _viewModel;
        private readonly TaskCompletionSource _loadedTcs = new();

        public Task WhenLoaded => _loadedTcs.Task;

        public PlanPeriodView(PlanPeriodViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += (_, _) => EntranceAnimation.PlayFadeSlideIn(this);
            Loaded += async (_, _) =>
            {
                await viewModel.LoadPeriodsAsync();
                _loadedTcs.TrySetResult();
            };
        }

        private async void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await _viewModel.LoadProductsAsync();
        }

        private async void QuantityBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { Tag: PlanPeriodProductRow row }) return;

            if (!int.TryParse(row.QuantityText, out var quantity) || quantity < 0)
            {
                row.QuantityText = "0";
                quantity = 0;
            }
            else
            {
                row.QuantityText = quantity.ToString();
            }

            try
            {
                await _viewModel.SaveQuantityAsync(row.ProductId, quantity);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ الكمية");
            }
        }
    }
}
