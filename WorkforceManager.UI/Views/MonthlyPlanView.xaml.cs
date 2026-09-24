using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الخطة الشهرية" — الحفظ عند فقدان تركيز خانة الكمية (نفس نمط
    /// WorkerOrderDialog)، مفيش زرار "حفظ" عام.
    /// </summary>
    public partial class MonthlyPlanView : UserControl
    {
        private readonly MonthlyPlanViewModel _viewModel;

        public MonthlyPlanView(MonthlyPlanViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += async (_, _) => await viewModel.LoadAsync();
        }

        private async void QuantityBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { Tag: MonthlyPlanProductRow row } textBox) return;

            // نص فاضي أو غير رقمي = صفر — Quantity بيرجع 0 تلقائيًا (شوف MonthlyPlanProductRow)،
            // بس النص المعروض لازم يتظبط برضه عشان المستخدم يشوف "0" مش سطر فاضي
            if (!int.TryParse(row.QuantityText, out var quantity) || quantity < 0)
            {
                row.QuantityText = "0";
                quantity = 0;
            }
            else
            {
                // إعادة كتابة النص المظبوط (بيشيل أصفار زيادة زي "007") — بعد الـTryParse فوق
                row.QuantityText = quantity.ToString();
            }

            var group = _viewModel.FamilyGroups.FirstOrDefault(g => g.Products.Contains(row));
            if (group is null) return;

            try
            {
                await _viewModel.SaveQuantityAsync(row.ProductId, quantity);
                _viewModel.OnQuantitySaved(group);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ الكمية");
            }
        }
    }
}
