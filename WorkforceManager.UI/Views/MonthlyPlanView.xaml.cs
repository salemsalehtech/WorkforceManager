using System.Collections.Generic;
using System.Linq;
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
        private readonly TaskCompletionSource _loadedTcs = new();

        /// <summary>بيتحل لما LoadAsync الذاتي يخلص — نفس سبب ReportBuilderView.WhenLoaded بالظبط</summary>
        public Task WhenLoaded => _loadedTcs.Task;

        public MonthlyPlanView(MonthlyPlanViewModel viewModel)
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

        /// <summary>
        /// "الخطة الشهرية" من قائمة كارت منتج: مؤشر الكيبورد على خانة الكمية
        /// بتاعته والنص متحدد. تدوير على شجرة العرض (زي DailyEntryView
        /// FocusedFlow) لأن الصفوف جوّه ItemsControl من غير x:Name خارجي.
        /// </summary>
        public void FocusProductQuantity(int productId)
        {
            var box = FindDescendants<TextBox>(this)
                .FirstOrDefault(t => t.Name == "QuantityBox" && t.Tag is MonthlyPlanProductRow row && row.ProductId == productId);
            if (box is null) return;

            box.Focus();
            box.SelectAll();
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindDescendants<T>(child)) yield return descendant;
            }
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

            // الخطأ تحت الخانة نفسها بدل إشعار طاير — والقيمة الغلط مابتتحفظش
            var error = FieldRules.OptionalNonNegativeInt(text, "الخطة اليومية لازم تكون رقم صحيح أو فاضية");
            if (error.Length > 0)
            {
                row.DailyTargetError = error;
                return;
            }

            int? target = null;
            if (text.Length > 0)
            {
                target = int.Parse(text);
                row.DailyTargetText = target.Value.ToString(); // بيشيل أصفار زيادة زي "007"
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
