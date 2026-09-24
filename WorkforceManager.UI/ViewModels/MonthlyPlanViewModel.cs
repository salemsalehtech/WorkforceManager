using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الخطة الشهرية" — دخول الكمية المخططة لكل منتج، لشهر
    /// واحد. **تخطيط بس**: مفيش تحقيق ولا مقارنة بإنتاج فعلي هنا (فيتشر
    /// منفصل لاحق). خطة العيلة دايمًا SUM محسوب، مفيش قيمة بتتكتب عليها.
    /// </summary>
    public partial class MonthlyPlanViewModel : ObservableObject
    {
        private static readonly string[] ArabicMonthNames =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        private readonly IServiceScopeFactory _scopeFactory;

        public MonthlyPlanViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            var today = DateTime.Today;
            _selectedYear = today.Year;
            _selectedMonth = today.Month;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MonthLabel))]
        private int _selectedYear;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MonthLabel))]
        private int _selectedMonth;

        public string MonthLabel => $"{ArabicMonthNames[SelectedMonth - 1]} {SelectedYear}";

        [ObservableProperty] private bool _isBusy;

        public ObservableCollection<MonthlyPlanFamilyGroupRow> FamilyGroups { get; } = new();

        public int GrandTotal => FamilyGroups.Sum(g => g.Subtotal);

        private void RefreshGrandTotal() => OnPropertyChanged(nameof(GrandTotal));

        [RelayCommand]
        private async Task PreviousMonthAsync()
        {
            (SelectedYear, SelectedMonth) = SelectedMonth == 1 ? (SelectedYear - 1, 12) : (SelectedYear, SelectedMonth - 1);
            await LoadAsync();
        }

        [RelayCommand]
        private async Task NextMonthAsync()
        {
            (SelectedYear, SelectedMonth) = SelectedMonth == 12 ? (SelectedYear + 1, 1) : (SelectedYear, SelectedMonth + 1);
            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MonthlyPlanService>();
                var products = await service.GetForMonthAsync(SelectedYear, SelectedMonth);

                FamilyGroups.Clear();

                foreach (var g in products
                             .Where(p => p.FamilyId is not null)
                             .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                             .OrderBy(g => g.Key.Item2))
                {
                    FamilyGroups.Add(new MonthlyPlanFamilyGroupRow
                    {
                        HeaderText = $"{g.Key.Item2} ({g.Count()})",
                        Products = g.Select(ToRow).ToList()
                    });
                }

                var noFamily = products.Where(p => p.FamilyId is null).Select(ToRow).ToList();
                if (noFamily.Count > 0)
                    FamilyGroups.Add(new MonthlyPlanFamilyGroupRow
                    {
                        HeaderText = $"بدون عيلة ({noFamily.Count})", Products = noFamily
                    });

                RefreshGrandTotal();
            }
            finally { IsBusy = false; }
        }

        private static MonthlyPlanProductRow ToRow(Business.DTOs.MonthlyPlanProductDto p) => new()
        {
            ProductId = p.ProductId, ProductName = p.ProductName, IsComplete = p.IsComplete,
            QuantityText = p.PlannedQuantity.ToString()
        };

        /// <summary>بيتنادى من الشاشة (LostFocus) بعد ما قيمة اتحفظت — بيحدّث Subtotal/GrandTotal محليًا من غير إعادة تحميل</summary>
        public void OnQuantitySaved(MonthlyPlanFamilyGroupRow group)
        {
            group.RefreshSubtotal();
            RefreshGrandTotal();
        }

        public async Task SaveQuantityAsync(int productId, int quantity)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                .SetPlanAsync(productId, SelectedYear, SelectedMonth, quantity);
        }

        /// <summary>
        /// "انسخ خطة الشهر اللي فات" — بيسأل تأكيد بس لو الشهر ده فيه صفوف
        /// بالفعل (Upsert هيستبدلها)، بعدين يعيد تحميل الشاشة عشان القيم
        /// الجديدة تبان.
        /// </summary>
        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task CopyFromPreviousMonthAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<MonthlyPlanService>();

            if (await service.MonthHasEntriesAsync(SelectedYear, SelectedMonth))
            {
                var confirmed = Notify.Ask(
                    "الشهر ده فيه قيم متسجلة بالفعل — نسخ خطة الشهر اللي فات هيستبدلها. تكمل؟",
                    "تأكيد الاستبدال");
                if (!confirmed) return;
            }

            var copiedCount = await service.CopyFromPreviousMonthAsync(SelectedYear, SelectedMonth);
            if (copiedCount == 0)
                Notify.Info("الشهر اللي فات مفيش فيه خطة تتنسخ", "مفيش حاجة");

            await LoadAsync();
        }
    }
}
