using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// تبويب "الإحصائيات" في الخطة الشهرية — لوحة تحليل كاملة للشهر: إنجاز
    /// وإيقاع، توقّع، المطلوب يوميًا، مقارنة بالشهر اللي فات، رسم تراكمي/يومي،
    /// حالة المنتجات، إيقاع أيام الأسبوع، العيلات، والمنتجات اللي محتاجة
    /// اهتمام. كل الأرقام من MonthlyPlanStatisticsService (اللي بيجمع صفوف
    /// MonthlyPlanTrackingService نفسها) — مفيش حساب هنا غير التنسيق.
    /// </summary>
    public partial class StatisticsViewModel : ObservableObject
    {
        private static readonly string[] ArabicMonthNames =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        private static readonly Dictionary<DayOfWeek, string> WeekdayNames = new()
        {
            [DayOfWeek.Saturday] = "السبت", [DayOfWeek.Sunday] = "الأحد", [DayOfWeek.Monday] = "الاتنين",
            [DayOfWeek.Tuesday] = "التلات", [DayOfWeek.Wednesday] = "الأربع", [DayOfWeek.Thursday] = "الخميس",
            [DayOfWeek.Friday] = "الجمعة"
        };

        /// <summary>عدد المنتجات في قايمتي "محتاج اهتمام" و"الأفضل أداءً"</summary>
        private const int TopListSize = 5;

        /// <summary>أطول عمود في "إيقاع الأسبوع" بالبكسل</summary>
        private const double WeekdayBarMaxHeight = 96;

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

        /// <summary>آخر نتيجة — كل الخصايص المحسوبة تحت بتقرا منها</summary>
        [ObservableProperty] private MonthlyPlanStatisticsDto? _stats;

        /// <summary>
        /// تقريبًا كل خاصية في الشاشة مشتقة من Stats — تحديث شامل (string.Empty)
        /// بدل قايمة NotifyPropertyChangedFor بخمسين اسم بتتنسي كل ما كارت يتضاف
        /// </summary>
        partial void OnStatsChanged(MonthlyPlanStatisticsDto? value) => OnPropertyChanged(string.Empty);

        // ═══════════ الفلتر: عيلة / مادة ═══════════

        public ObservableCollection<StatisticsFilterOption> FamilyOptions { get; } = new();
        public ObservableCollection<StatisticsFilterOption> MaterialOptions { get; } = new();

        [ObservableProperty] private StatisticsFilterOption? _selectedFamilyOption;
        [ObservableProperty] private StatisticsFilterOption? _selectedMaterialOption;

        /// <summary>بيوقف إعادة التحميل وإحنا بنملى القوايم من النتيجة نفسها</summary>
        private bool _suppressFilterReload;

        async partial void OnSelectedFamilyOptionChanged(StatisticsFilterOption? value) => await ReloadForFilterAsync();
        async partial void OnSelectedMaterialOptionChanged(StatisticsFilterOption? value) => await ReloadForFilterAsync();

        private async Task ReloadForFilterAsync()
        {
            if (_suppressFilterReload) return;
            await LoadAsync();
        }

        private MonthlyPlanStatisticsFilter CurrentFilter() => new(
            SelectedFamilyOption?.FamilyName,
            SelectedMaterialOption?.Material,
            SelectedMaterialOption is { IsAll: false });

        public bool IsFiltered => Stats?.Filter.IsActive == true;

        [RelayCommand]
        private void ClearFilter()
        {
            _suppressFilterReload = true;
            SelectedFamilyOption = FamilyOptions.FirstOrDefault();
            SelectedMaterialOption = MaterialOptions.FirstOrDefault();
            _suppressFilterReload = false;
            _ = LoadAsync();
        }

        private void RefreshFilterOptions(MonthlyPlanStatisticsDto stats)
        {
            _suppressFilterReload = true;
            try
            {
                var family = stats.Filter.FamilyName;
                FamilyOptions.Clear();
                FamilyOptions.Add(StatisticsFilterOption.AllFamilies);
                foreach (var name in stats.AvailableFamilies) FamilyOptions.Add(StatisticsFilterOption.ForFamily(name));
                SelectedFamilyOption = FamilyOptions.FirstOrDefault(o => o.FamilyName == family) ?? FamilyOptions[0];

                MaterialOptions.Clear();
                MaterialOptions.Add(StatisticsFilterOption.AllMaterials);
                foreach (var material in stats.AvailableMaterials) MaterialOptions.Add(StatisticsFilterOption.ForMaterial(material));
                SelectedMaterialOption = stats.Filter.FilterByMaterial
                    ? MaterialOptions.FirstOrDefault(o => !o.IsAll && o.Material == stats.Filter.Material) ?? MaterialOptions[0]
                    : MaterialOptions[0];
            }
            finally { _suppressFilterReload = false; }
        }

        // ═══════════ التصدير ═══════════

        [RelayCommand]
        private async Task ExportExcelAsync()
        {
            if (Stats is not { HasAnyData: true } stats)
            {
                Notify.Warn("مفيش بيانات في الشهر ده للتصدير");
                return;
            }

            var filterText = stats.Filter.IsActive ? " — " + FilterLabel : "";
            await ExcelExport.RunAsync(
                "تصدير الإحصائيات", $"إحصائيات {MonthLabel}{filterText} - {DateTime.Today:yyyy-MM-dd}",
                filePath =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<MonthlyPlanStatisticsExcelService>()
                        .Export(stats, $"{MonthLabel}{filterText} — لحد {stats.AsOfDate:yyyy/MM/dd}", filePath);
                    return Task.CompletedTask;
                });
        }

        private string FilterLabel => string.Join(" · ", new[]
        {
            SelectedFamilyOption is { IsAll: false } f ? f.Label : null,
            SelectedMaterialOption is { IsAll: false } m ? m.Label : null
        }.Where(s => s is not null));


        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDailyChart), nameof(PlanLegendText))]
        private bool _isCumulativeChart = true;

        public bool IsDailyChart => !IsCumulativeChart;

        public string PlanLegendText => IsCumulativeChart ? "المفروض حسب الخطة" : "المعدل اليومي المخطط";

        public ObservableCollection<StatisticsProductRow> NeedsAttention { get; } = new();
        public ObservableCollection<StatisticsProductRow> TopPerformers { get; } = new();
        public ObservableCollection<StatisticsProductRow> ProductComparisons { get; } = new();
        public ObservableCollection<StatisticsFamilyRow> Families { get; } = new();
        public ObservableCollection<StatisticsWeekdayRow> Weekdays { get; } = new();
        public ObservableCollection<MonthlyPlanStatisticsMismatchDto> Mismatches { get; } = new();
        public ObservableCollection<StatisticsProductRow> TopScrap { get; } = new();

        [ObservableProperty] private bool _hasNeedsAttention;
        [ObservableProperty] private bool _hasTopPerformers;
        [ObservableProperty] private bool _hasFamilies;
        [ObservableProperty] private bool _hasComparisons;
        [ObservableProperty] private bool _hasMismatches;
        [ObservableProperty] private bool _hasTopScrap;

        // ═══════════ الحالة العامة ═══════════

        public bool HasData => Stats?.HasAnyData == true;
        public bool HasPlan => Stats?.TotalPlanned > 0;

        public DateTime AsOfDate => Stats?.AsOfDate ?? DateTime.Today;

        public string AsOfText => Stats is null ? "" : $"محسوبة لحد {Stats.AsOfDate.Day} {ArabicMonthNames[Stats.AsOfDate.Month - 1]}";

        public string WorkdaysText => Stats is null ? "" : $"{Stats.ElapsedWorkdays} من {Stats.TotalWorkdays} يوم شغل";

        public string RemainingWorkdaysText => Stats is null ? "" : $"فاضل {Stats.RemainingWorkdays} يوم";

        // ═══════════ الكارت الرئيسي: نسبة الإنجاز ═══════════

        public string CompletionText => Stats?.CompletionPercent is { } c ? StatisticsStatusKeys.Percent(c) : "—";

        /// <summary>شريط الإنجاز: عمودين نجمة (المحقق / الباقي)، مقصوص عند 100%</summary>
        public GridLength CompletionColumn => StatisticsStatusKeys.BarColumn(Stats?.CompletionPercent);
        public GridLength CompletionRestColumn => StatisticsStatusKeys.BarRestColumn(Stats?.CompletionPercent);

        public string AchievedOfPlanText => Stats is null ? ""
            : HasPlan ? $"{Stats.TotalAchieved:N0} من {Stats.TotalPlanned:N0} قطعة"
            : $"{Stats.TotalAchieved:N0} قطعة — مفيش خطة متسجلة للشهر ده";

        public string ExpectedText => Stats is null ? "" : "المفروض لحد النهارده " + StatisticsStatusKeys.Percent(Stats.ExpectedPercent);

        /// <summary>مكان علامة "المفروض" على شريط الإنجاز — عمودين نجمة</summary>
        public GridLength ExpectedColumn => new((double)(Stats?.ExpectedPercent ?? 0), GridUnitType.Star);
        public GridLength RemainingColumn => new((double)(1 - (Stats?.ExpectedPercent ?? 0)), GridUnitType.Star);

        private PlanPaceStatus PaceStatus => MonthlyPlanStatisticsMath.StatusOf(Stats?.PacePercent);

        public string PaceKey => StatisticsStatusKeys.Ink(PaceStatus);

        public string PaceSentence
        {
            get
            {
                if (Stats?.PacePercent is null) return HasPlan ? "لسه مفيش أيام شغل عدّت" : "";
                return PaceStatus switch
                {
                    PlanPaceStatus.Behind => $"متأخرين عن الإيقاع بـ {Math.Abs(Stats.PaceGap):N0} قطعة",
                    PlanPaceStatus.Ahead => $"سابقين الإيقاع بـ {Stats.PaceGap:N0} قطعة",
                    _ => Stats.PaceGap >= 0
                        ? $"ماشيين مع الإيقاع (+{Stats.PaceGap:N0})"
                        : $"ماشيين مع الإيقاع ({Stats.PaceGap:N0})"
                };
            }
        }

        // ═══════════ كروت المؤشرات ═══════════

        public string PaceText => Stats?.PacePercent is { } p ? StatisticsStatusKeys.Percent(p) : "—";
        public string PaceCaption => Stats?.PacePercent is null ? "مفيش خطة للمقارنة" : StatisticsStatusKeys.Label(PaceStatus) + " — المحقق ÷ المفروض";

        public string ForecastText => Stats?.Forecast is { } f ? $"{f:N0}" : "—";
        public string ForecastCaption => Stats?.ForecastGap switch
        {
            null => Stats?.Forecast is null ? "لسه بدري على التوقّع" : "بالإيقاع الحالي",
            < 0 and var gap => $"أقل من الخطة بـ {Math.Abs(gap):N0}",
            0 => "على الخطة بالظبط",
            var gap => $"فوق الخطة بـ {gap:N0}"
        };
        public string ForecastKey => Stats?.ForecastGap switch
        {
            null => "TextMutedBrush",
            < 0 => "DangerBrush",
            _ => "GoldDeepBrush"
        };

        public string RequiredDailyText => Stats?.RequiredDaily is { } r ? $"{r:N0}" : "—";
        public string RequiredDailyCaption => Stats?.AverageDaily is { } avg
            ? $"متوسطك الحالي {avg:N0} / يوم"
            : Stats?.RequiredDaily is null ? "مفيش أيام باقية" : "لسه مفيش متوسط";
        public string RequiredDailyKey =>
            Stats is { RequiredDaily: { } r, AverageDaily: { } avg } && r > avg * 1.1m ? "DangerBrush"
            : Stats?.RequiredDaily is null ? "TextMutedBrush" : "GoldDeepBrush";

        public string ChangeText => StatisticsStatusKeys.Change(Stats?.ChangeVsPrevious);
        public string ChangeCaption => Stats is null ? ""
            : Stats.PreviousSameDay > 0 ? $"{Stats.PreviousSameDay:N0} في نفس اليوم من الشهر اللي فات"
            : "مفيش إنتاج الشهر اللي فات للمقارنة";
        public string ChangeKey => StatisticsStatusKeys.ChangeKey(Stats?.ChangeVsPrevious);

        public string BestDayText => Stats?.BestDayDate is null ? "—" : $"{Stats.BestDayQuantity:N0}";
        public string BestDayCaption => Stats?.BestDayDate is { } d
            ? $"{WeekdayNames[d.DayOfWeek]} {d.Day} {ArabicMonthNames[d.Month - 1]}"
            : "لسه مفيش إنتاج";

        public string WeightText => Stats is null || Stats.TotalWeightGrams == 0 ? "—" : StatisticsStatusKeys.Weight(Stats.TotalWeightGrams);
        public string WeightCaption => Stats?.ProductsWithoutWeight is > 0 and var n
            ? $"{n} منتج من غير وزن مسجّل"
            : "وزن كل المحقق";

        public string ConsistencyText => Stats is null || Stats.ElapsedWorkdays == 0 ? "—"
            : $"{Stats.RecordedWorkdays} / {Stats.ElapsedWorkdays}";
        public string ConsistencyCaption => Stats is null || Stats.ElapsedWorkdays == 0 ? "لسه مفيش أيام شغل عدّت"
            : Stats.RecordedWorkdays == Stats.ElapsedWorkdays ? "كل أيام الشغل متسجّلة"
            : $"{Stats.ElapsedWorkdays - Stats.RecordedWorkdays} يوم شغل من غير تسجيل";
        public string ConsistencyKey => Stats is { ElapsedWorkdays: > 0 } s && s.RecordedWorkdays < s.ElapsedWorkdays
            ? "WarnBrush" : "GoldDeepBrush";

        // ── متوسط نفس اليوم في آخر 3 شهور ──
        public string HistoryChangeText => StatisticsStatusKeys.Change(Stats?.ChangeVsHistoryAverage);
        public string HistoryChangeKey => StatisticsStatusKeys.ChangeKey(Stats?.ChangeVsHistoryAverage);
        public string HistoryCaption => Stats?.HistoryAverageSameDay is { } avg
            ? $"متوسط {avg:N0} في نفس اليوم (آخر {Stats.HistoryMonthsCounted} شهور)"
            : "مفيش تسجيل في الـ3 شهور اللي فاتوا";

        // ── الهالك (من رحلة الإنتاج) ──
        public string ScrapText => Stats?.ScrapPercent is { } p ? StatisticsStatusKeys.Percent1(p) : "—";
        public string ScrapCaption => Stats is { TotalRealCompleted: > 0 } or { TotalScrap: > 0 }
            ? $"{Stats!.TotalScrap:N0} قطعة هالك من {Stats.TotalRealCompleted + Stats.TotalScrap:N0}"
            : "مفيش إنتاج متسجّل في رحلة الإنتاج";
        public string ScrapKey => Stats?.ScrapPercent is >= 0.05m ? "DangerBrush" : "GoldDeepBrush";

        // ── اليدوي مقابل الحقيقي ──
        public string ManualVsRealText => StatisticsStatusKeys.Change(Stats?.ManualVsRealChange);
        public string ManualVsRealCaption => Stats is null ? ""
            : Stats.TotalRealCompleted == 0 ? "مفيش إنتاج حقيقي للمقارنة"
            : $"يدوي {Stats.TotalAchieved:N0} · حقيقي {Stats.TotalRealCompleted:N0}";
        public string ManualVsRealKey => Stats?.ManualVsRealChange is { } c && Math.Abs(c) > MonthlyPlanStatisticsMath.MismatchThreshold * 100
            ? "WarnBrush" : "GoldDeepBrush";

        // ── إنتاجية العامل ──
        public string PerWorkerText => Stats?.OutputPerPresentWorker is { } w ? $"{w:N1}" : "—";
        public string PerWorkerCaption => Stats switch
        {
            { HasAttendanceData: false } => "مش متاحة مع الفلتر",
            { AveragePresentWorkers: { } n } => $"قطعة / عامل / يوم · متوسط {n:N0} حاضر",
            _ => "مفيش حضور متسجّل"
        };

        // ═══════════ حالة المنتجات ═══════════

        public int AheadCount => Stats?.AheadCount ?? 0;
        public int OnTrackCount => Stats?.OnTrackCount ?? 0;
        public int BehindCount => Stats?.BehindCount ?? 0;
        public int OutsidePlanCount => Stats?.OutsidePlanCount ?? 0;
        public GridLength AheadWidth => new(AheadCount, GridUnitType.Star);
        public GridLength OnTrackWidth => new(OnTrackCount, GridUnitType.Star);
        public GridLength BehindWidth => new(BehindCount, GridUnitType.Star);
        public GridLength OutsidePlanWidth => new(OutsidePlanCount, GridUnitType.Star);
        public bool HasStatusBreakdown => AheadCount + OnTrackCount + BehindCount + OutsidePlanCount > 0;

        // ═══════════ الرسم ═══════════

        public IReadOnlyList<MonthlyPlanStatisticsDayDto> ChartDays => Stats?.Days ?? new List<MonthlyPlanStatisticsDayDto>();

        /// <summary>خط "المعدل المخطط" في الوضع اليومي — الخطة ÷ أيام الشغل</summary>
        public double DailyPlanRate => Stats is { TotalWorkdays: > 0 } s ? (double)s.TotalPlanned / s.TotalWorkdays : 0;

        [RelayCommand]
        private void ShowCumulativeChart() => IsCumulativeChart = true;

        [RelayCommand]
        private void ShowDailyChart() => IsCumulativeChart = false;

        // ═══════════ إيقاع الأسبوع ═══════════

        public bool HasWeekdayData => Weekdays.Any(w => w.WorkdayCount > 0);

        public string WeekdayInsight
        {
            get
            {
                var active = Weekdays.Where(w => w.WorkdayCount > 0).ToList();
                if (active.Count < 2) return "";
                var best = active.MaxBy(w => w.Average)!;
                var worst = active.MinBy(w => w.Average)!;
                if (best.Average == worst.Average) return "الإنتاج متساوي على كل أيام الأسبوع";
                return $"أعلى متوسط يوم {best.DayName}، وأقل متوسط يوم {worst.DayName}";
            }
        }

        // ═══════════ التنقل ═══════════

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

        [RelayCommand]
        private async Task CurrentMonthAsync()
        {
            (SelectedYear, SelectedMonth) = (DateTime.Today.Year, DateTime.Today.Month);
            await LoadAsync();
        }

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var monthEnd = new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(1).AddDays(-1);
                var asOfDate = DateTime.Today < monthEnd ? DateTime.Today : monthEnd;

                Apply(await scope.ServiceProvider.GetRequiredService<MonthlyPlanStatisticsService>()
                    .GetAsync(SelectedYear, SelectedMonth, asOfDate, CurrentFilter()));
            }
            catch (Exception ex)
            {
                Notify.Error("تعذّر تحميل الإحصائيات: " + ex.Message);
            }
            finally { IsBusy = false; }
        }

        /// <summary>بيبني كل قوايم الشاشة من نتيجة واحدة — منفصل عن التحميل عشان يتختبر من غير قاعدة بيانات</summary>
        public void Apply(MonthlyPlanStatisticsDto stats)
        {
            var rows = stats.Products
                .Select(p => StatisticsProductRow.FromDto(p, stats.RealByProduct.GetValueOrDefault(p.ProductId)))
                .ToList();

            Reset(NeedsAttention, rows
                .Where(r => r.Planned > 0 && r.Status == PlanPaceStatus.Behind)
                .OrderBy(r => r.PacePercent).ThenBy(r => r.Gap)
                .Take(TopListSize));

            Reset(TopPerformers, rows
                .Where(r => r.Planned > 0 && r.PacePercent is not null && r.EffectiveAchieved > 0 && r.Status != PlanPaceStatus.Behind)
                .OrderByDescending(r => r.PacePercent).ThenByDescending(r => r.EffectiveAchieved)
                .Take(TopListSize));

            Reset(ProductComparisons, rows
                .Where(r => r.EffectiveAchieved != 0 || r.SameDayPreviousMonth != 0)
                .OrderByDescending(r => r.EffectiveAchieved).ThenBy(r => r.ProductName));

            Reset(Families, stats.Families.Select(StatisticsFamilyRow.FromDto));

            var maxAverage = stats.Weekdays.Select(w => w.Average).DefaultIfEmpty(0).Max();
            Reset(Weekdays, stats.Weekdays.Select(w => new StatisticsWeekdayRow
            {
                DayName = WeekdayNames[w.Day],
                Average = w.Average,
                WorkdayCount = w.WorkdayCount,
                BarHeight = maxAverage > 0 ? Math.Max(3, (double)(w.Average / maxAverage) * WeekdayBarMaxHeight) : 3,
                IsBest = maxAverage > 0 && w.Average == maxAverage
            }));

            HasNeedsAttention = NeedsAttention.Count > 0;
            HasTopPerformers = TopPerformers.Count > 0;
            HasFamilies = Families.Count > 0;
            HasComparisons = ProductComparisons.Count > 0;

            Reset(Mismatches, stats.Mismatches.Take(TopListSize));
            HasMismatches = Mismatches.Count > 0;

            Reset(TopScrap, rows
                .Where(r => r.ScrapPieces > 0)
                .OrderByDescending(r => r.ScrapPercent).ThenByDescending(r => r.ScrapPieces)
                .Take(TopListSize));
            HasTopScrap = TopScrap.Count > 0;

            RefreshFilterOptions(stats);

            // آخر حاجة عشان كل الخصايص المحسوبة (WeekdayInsight وغيرها) تقرا القوايم الجديدة
            Stats = stats;
        }

        private static void Reset<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();
            foreach (var item in items) target.Add(item);
        }
    }
}
