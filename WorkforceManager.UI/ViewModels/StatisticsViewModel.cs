using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// شاشة "الإحصائيات" — كل التفاصيل التحليلية اللي كانت على شاشة الخطة
    /// الشهرية قبل كده (مقارنة الشهر اللي فات، تفاصيل الوزن الفرعية) زائد
    /// رسم بياني لإنتاج الشهر، بمعزل تام عن شاشة الخطة نفسها. تقرأ نفس
    /// بيانات MonthlyPlanTrackingService، مفيش حساب مختلف هنا. شوف CLAUDE.md.
    /// </summary>
    public partial class StatisticsViewModel : ObservableObject
    {
        private static readonly string[] ArabicMonthNames =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        private readonly IServiceScopeFactory _scopeFactory;

        public StatisticsViewModel(IServiceScopeFactory scopeFactory)
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

        public ObservableCollection<ChartBucket> ChartBuckets { get; } = new();
        [ObservableProperty] private bool _chartHasData;

        public ObservableCollection<StatisticsProductRow> ProductComparisons { get; } = new();
        public ObservableCollection<StatisticsFamilyWeightRow> FamilyWeights { get; } = new();
        [ObservableProperty] private bool _hasFamilyWeights;

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
                var monthEnd = new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(1).AddDays(-1);
                var asOfDate = DateTime.Today.Year == SelectedYear && DateTime.Today.Month == SelectedMonth
                    ? DateTime.Today : monthEnd;

                var tracking = await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                    .GetTrackingAsync(SelectedYear, SelectedMonth, asOfDate);

                ProductComparisons.Clear();
                foreach (var row in tracking.OrderBy(p => p.ProductName).Select(StatisticsProductRow.FromDto))
                    ProductComparisons.Add(row);

                FamilyWeights.Clear();
                foreach (var g in tracking
                             .Where(p => p.PieceWeightGrams is not null)
                             .GroupBy(p => p.FamilyName ?? "بدون عيلة")
                             .OrderBy(g => g.Key))
                {
                    var totalGrams = g.Sum(p => p.PieceWeightGrams!.Value * p.EffectiveAchieved);
                    FamilyWeights.Add(new StatisticsFamilyWeightRow { FamilyName = g.Key, TotalWeightGrams = totalGrams });
                }
                HasFamilyWeights = FamilyWeights.Count > 0;

                var monthStart = new DateTime(SelectedYear, SelectedMonth, 1);
                var points = await scope.ServiceProvider.GetRequiredService<ProductionChartService>()
                    .GetProductOutputAsync(monthStart, monthEnd, ChartGrain.Day);

                var chart = ProductOutputChartBuilder.Build(
                    points, monthStart, monthEnd, ChartGrain.Day, new Dictionary<int, int>(), maxBarHeight: 160);

                ChartBuckets.Clear();
                foreach (var bucket in chart.Buckets) ChartBuckets.Add(bucket);
                ChartHasData = chart.HasData;
            }
            finally { IsBusy = false; }
        }
    }
}
