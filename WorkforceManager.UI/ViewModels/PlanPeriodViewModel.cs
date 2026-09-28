using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "خطة بفترة مخصصة" — دخول الكمية المخططة بس لفترة start/end
    /// حرة، بديل موازي للخطة الشهرية (شوف CLAUDE.md). مفيش تتبّع/محقق هنا
    /// عن قصد — نفس حدود المرحلة الأولى اللي MonthlyPlan نفسها بدأت بيها.
    /// </summary>
    public partial class PlanPeriodViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PlanPeriodViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public ObservableCollection<PlanPeriodOption> Periods { get; } = new();

        [ObservableProperty] private PlanPeriodOption? _selectedPeriod;

        [ObservableProperty] private bool _isBusy;

        public ObservableCollection<PlanPeriodFamilyGroupRow> FamilyGroups { get; } = new();

        public int GrandTotalPlan => FamilyGroups.Sum(g => g.Subtotal);

        public bool HasPeriods => Periods.Count > 0;

        public async Task LoadPeriodsAsync()
        {
            var previouslySelectedId = SelectedPeriod?.Id;

            using var scope = _scopeFactory.CreateScope();
            var periods = await scope.ServiceProvider.GetRequiredService<PlanPeriodService>().GetPeriodsAsync();

            Periods.Clear();
            foreach (var period in periods)
                Periods.Add(new PlanPeriodOption { Period = period });

            OnPropertyChanged(nameof(HasPeriods));

            SelectedPeriod = Periods.FirstOrDefault(p => p.Id == previouslySelectedId) ?? Periods.FirstOrDefault();
            await LoadProductsAsync();
        }

        public async Task LoadProductsAsync()
        {
            FamilyGroups.Clear();
            OnPropertyChanged(nameof(GrandTotalPlan));

            if (SelectedPeriod is null) return;

            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var products = await scope.ServiceProvider.GetRequiredService<PlanPeriodService>()
                    .GetForPeriodAsync(SelectedPeriod.Id);

                foreach (var group in BuildFamilyGroups(products))
                    FamilyGroups.Add(group);

                OnPropertyChanged(nameof(GrandTotalPlan));
            }
            finally { IsBusy = false; }
        }

        private static List<PlanPeriodFamilyGroupRow> BuildFamilyGroups(IEnumerable<PlanPeriodProductDto> products)
        {
            var groups = new List<PlanPeriodFamilyGroupRow>();

            foreach (var g in products
                         .Where(p => p.FamilyId is not null)
                         .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                         .OrderBy(g => g.Key.Item2))
            {
                groups.Add(new PlanPeriodFamilyGroupRow
                {
                    HeaderText = $"{g.Key.Item2} ({g.Count()})",
                    Products = g.OrderBy(p => p.ProductName).Select(PlanPeriodProductRow.FromDto).ToList()
                });
            }

            var noFamily = products.Where(p => p.FamilyId is null).OrderBy(p => p.ProductName).ToList();
            if (noFamily.Count > 0)
                groups.Add(new PlanPeriodFamilyGroupRow
                {
                    HeaderText = $"بدون عيلة ({noFamily.Count})",
                    Products = noFamily.Select(PlanPeriodProductRow.FromDto).ToList()
                });

            return groups;
        }

        public async Task SaveQuantityAsync(int productId, int quantity)
        {
            if (SelectedPeriod is null) return;

            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PlanPeriodService>()
                .SetTargetAsync(SelectedPeriod.Id, productId, quantity);

            // الخطة اتغيّرت — مجموع العيلة (Subtotal) لازم يتحسب من جديد
            await LoadProductsAsync();
        }

        /// <summary>"فترة جديدة" — بتفتح ديالوج الإدخال، وبعد الحفظ بتختارها تلقائيًا</summary>
        [RelayCommand]
        private async Task NewPeriodAsync()
        {
            var dialog = new PlanPeriodDialog(_scopeFactory) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var period = await scope.ServiceProvider.GetRequiredService<PlanPeriodService>()
                    .CreatePeriodAsync(dialog.SelectedStart, dialog.SelectedEnd);

                await LoadPeriodsAsync();
                SelectedPeriod = Periods.FirstOrDefault(p => p.Id == period.Id);
                await LoadProductsAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إنشاء الفترة");
            }
        }

        /// <summary>
        /// "انسخ خطة الفترة اللي فاتت" — بيسأل تأكيد بس لو الفترة الحالية
        /// فيها صفوف بالفعل (Upsert هيستبدلها)، نفس نمط
        /// MonthlyPlanViewModel.CopyFromPreviousMonthAsync بالظبط.
        /// </summary>
        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task CopyFromPreviousPeriodAsync()
        {
            if (SelectedPeriod is null) return;

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PlanPeriodService>();

            if (await service.PeriodHasEntriesAsync(SelectedPeriod.Id))
            {
                var confirmed = Notify.Ask(
                    "الفترة دي فيها قيم متسجلة بالفعل — نسخ خطة الفترة اللي فاتت هيستبدلها. تكمل؟",
                    "تأكيد الاستبدال");
                if (!confirmed) return;
            }

            var copiedCount = await service.CopyFromPreviousPeriodAsync(SelectedPeriod.Id);
            if (copiedCount == 0)
                Notify.Info("مفيش فترة سابقة بخطة تتنسخ", "مفيش حاجة");

            await LoadProductsAsync();
        }
    }
}
