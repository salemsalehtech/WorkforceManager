using System.Windows;
using System.Windows.Controls;
using WorkforceManager.UI.ViewModels;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// شاشة "الخطة الشهرية" — الحفظ عند فقدان تركيز خانة الكمية/التصليح
    /// (نفس نمط WorkerOrderDialog)، مفيش زرار "حفظ" عام.
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
            if (sender is not TextBox { Tag: MonthlyPlanProductRow row }) return;

            // نص فاضي أو غير رقمي = صفر — Quantity بيرجع 0 تلقائيًا (شوف MonthlyPlanProductRow)،
            // بس النص المعروض لازم يتظبط برضه عشان المستخدم يشوف "0" مش سطر فاضي
            if (!int.TryParse(row.QuantityText, out var quantity) || quantity < 0)
            {
                row.QuantityText = "0";
                quantity = 0;
            }
            else
            {
                row.QuantityText = quantity.ToString(); // بيشيل أصفار زيادة زي "007"
            }

            var group = _viewModel.AllFamilyGroups.FirstOrDefault(g => g.Products.Contains(row));
            if (group is null) return;

            try
            {
                await _viewModel.SaveQuantityAsync(row.ProductId, quantity);
                // الخطة اتغيّرت — التتبّع (نسبة المحقق، المطلوب يوميًا...) كله مبني عليها،
                // فمحتاج إعادة تحميل كاملة مش تحديث محلي بس
                await _viewModel.LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ الكمية");
            }
        }

        private async void CorrectionBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { Tag: MonthlyPlanProductRow row }) return;

            if (!int.TryParse(row.CorrectionText, out var quantity))
            {
                row.CorrectionText = "0";
                quantity = 0;
            }
            else
            {
                row.CorrectionText = quantity.ToString();
            }

            try
            {
                await _viewModel.SaveCorrectionAsync(row.ProductId, quantity);
                // التصليح بيأثر على المحقق الفعلي والنسبة والمطلوب يوميًا — إعادة تحميل كاملة برضه
                await _viewModel.LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ التصليح");
            }
        }

        /// <summary>
        /// "الخطة اليومية" — هدف يومي يدوي، اختياري. عكس الكمية/التصليح،
        /// نص فاضي هنا معناه "مفيش هدف" (null)، مش صفر — فمفيش تطبيع لصفر
        /// زي باقي الحقول.
        /// </summary>
        private async void DailyTargetBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { Tag: MonthlyPlanProductRow row }) return;

            var text = row.DailyTargetText.Trim();
            int? target;

            if (text.Length == 0)
            {
                target = null;
            }
            else if (int.TryParse(text, out var parsed) && parsed >= 0)
            {
                target = parsed;
                row.DailyTargetText = parsed.ToString(); // بيشيل أصفار زيادة زي "007"
            }
            else
            {
                Notify.Warn("الخطة اليومية لازم تكون رقم صحيح أو فاضية", "قيمة غير صحيحة");
                return;
            }

            try
            {
                await _viewModel.SaveDailyTargetAsync(row.ProductId, target);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في حفظ الخطة اليومية");
            }
        }

        /// <summary>"لقطة يوم معيّن" (البند 9) — تغيير AsOfDate بيعيد حساب التتبّع كله لحد التاريخ ده</summary>
        private async void AsOfDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DatePicker { SelectedDate: { } date }) return;
            _viewModel.AsOfDate = date;
            await _viewModel.LoadAsync();
        }
    }
}
