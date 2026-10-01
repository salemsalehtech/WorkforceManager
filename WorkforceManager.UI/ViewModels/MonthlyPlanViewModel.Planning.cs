using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>فلتر كروت العيلات في تبويب التخطيط — بيحل محل بانر "عائلات واطية" وبادچات الحالة</summary>
    public enum PlanFamilyFilter { All, Behind, OnTrack, Ahead, ForecastBelow }

    public enum PlanFamilySort { LowestPercent, LargestPlan, Name }

    /// <summary>مادة واحدة في الشبكة بعد الفلتر/الترتيب — الإجماليات من المادة الأصلية كاملة، الكروت المفلترة بس</summary>
    public class MonthlyPlanMaterialView
    {
        public MonthlyPlanMaterialGroupRow Material { get; init; } = null!;
        public List<MonthlyPlanFamilyGroupRow> Families { get; init; } = new();

        /// <summary>"غير محدد" = منتجات من غير مادة — جنبها زرار "كمّل بيانات المنتجات"</summary>
        public bool IsUnclassified => Material.HeaderText == "غير محدد";
    }

    /// <summary>صف في "وضع الجدول": رأس عيلة أو منتج — ItemsControl واحد بقالبين حسب النوع</summary>
    public class MonthlyPlanTableHeader
    {
        public string Title { get; init; } = "";
        public string Totals { get; init; } = "";
    }

    /// <summary>
    /// تحسينات تبويب التخطيط: شريط مؤشرات، بحث/فلتر/ترتيب للكروت، تجميع حسب
    /// المادة أو من غيره، وضع جدول للإدخال السريع، ونتيجة الشهر للشهور اللي خلصت.
    /// كل ده عرض فوق نفس صفوف MaterialGroups — مفيش حساب تتبّع جديد هنا.
    /// </summary>
    public partial class MonthlyPlanViewModel
    {
        // ═══════════ شريط المؤشرات ═══════════

        private IEnumerable<MonthlyPlanProductRow> PlannedProducts =>
            AllFamilyGroups.SelectMany(g => g.Products).Where(p => p.Quantity > 0);

        public string KpiCompletionText => GrandTotalPlan > 0
            ? StatisticsStatusKeys.Percent((decimal)GrandTotalAchieved / GrandTotalPlan) : "—";

        public System.Windows.GridLength KpiCompletionColumn =>
            StatisticsStatusKeys.BarColumn(GrandTotalPlan > 0 ? (decimal)GrandTotalAchieved / GrandTotalPlan : 0);
        public System.Windows.GridLength KpiCompletionRestColumn =>
            StatisticsStatusKeys.BarRestColumn(GrandTotalPlan > 0 ? (decimal)GrandTotalAchieved / GrandTotalPlan : 0);

        public string KpiCompletionCaption => $"{GrandTotalAchieved:N0} من {GrandTotalPlan:N0} قطعة";

        private decimal? Pace
        {
            get
            {
                var proRated = PlannedProducts.Sum(p => p.ProRatedPlan);
                return proRated > 0 ? PlannedProducts.Sum(p => p.EffectiveAchieved) / proRated : null;
            }
        }

        public string KpiPaceText => Pace is { } p ? StatisticsStatusKeys.Percent(p) : "—";
        public string KpiPaceKey => StatisticsStatusKeys.Ink(MonthlyPlanStatisticsMath.StatusOf(Pace));
        public string KpiPaceCaption => Pace is null ? "لسه بدري" : StatisticsStatusKeys.Label(MonthlyPlanStatisticsMath.StatusOf(Pace)) + " — المحقق ÷ المفروض";

        private int? Forecast => PlannedProducts.Any() && PlannedProducts.All(p => p.ForecastEndOfMonth is not null)
            ? PlannedProducts.Sum(p => p.ForecastEndOfMonth!.Value) : null;

        public string KpiForecastLabel => IsClosedMonth ? "قفل الشهر على" : "توقّع نهاية الشهر";
        public string KpiForecastText => (IsClosedMonth ? GrandTotalAchieved : Forecast) is { } f ? f.ToString("N0") : "—";
        public string KpiForecastCaption
        {
            get
            {
                var value = IsClosedMonth ? GrandTotalAchieved : Forecast;
                if (value is not { } f || GrandTotalPlan == 0) return "";
                var gap = f - GrandTotalPlan;
                return gap >= 0 ? $"فوق الخطة بـ {gap:N0}" : $"أقل من الخطة بـ {-gap:N0}";
            }
        }
        public string KpiForecastKey => (IsClosedMonth ? GrandTotalAchieved : Forecast) is { } f && f < GrandTotalPlan ? "DangerBrush" : "GoldDeepBrush";

        /// <summary>الفترة قفلت (شهر فات أو "لحد يوم" على آخر يوم شغل)</summary>
        public bool IsClosedMonth => HasPlanForMonth && TotalWorkdays > 0 && RemainingWorkdays == 0;

        /// <summary>"0 يوم شغل متبقي من 26" في شهر خلص كانت بتلخبط — الشهر الخلصان بيقول كده صراحة</summary>
        public string WorkdaysChipText => IsClosedMonth
            ? $"الشهر خلص — {TotalWorkdays} يوم شغل"
            : $"{RemainingWorkdays} يوم شغل متبقي من {TotalWorkdays}";

        public string KpiRemainingText => IsClosedMonth ? "خلص" : RemainingWorkdays.ToString();
        public string KpiRemainingCaption => IsClosedMonth ? $"{TotalWorkdays} يوم شغل" : $"يوم شغل باقي من {TotalWorkdays}";

        // ═══════════ نتيجة الشهر (الشهور اللي خلصت) ═══════════

        public string MonthResultText
        {
            get
            {
                if (!IsClosedMonth || GrandTotalPlan == 0) return "";
                var families = AllFamilyGroups.Where(g => g.Subtotal > 0).ToList();
                var achievedFamilies = families.Count(g => g.AchievedSubtotal >= g.Subtotal);
                var worst = families
                    .Select(g => (g.FamilyName, Gap: g.Subtotal - g.AchievedSubtotal))
                    .Where(x => x.Gap > 0)
                    .OrderByDescending(x => x.Gap)
                    .FirstOrDefault();
                var text = $"قفلنا الشهر على {KpiCompletionText} من الخطة — {achievedFamilies} من {families.Count} عيلة حققت خطتها";
                return worst.FamilyName is null ? text : $"{text} — أكبر عجز: {worst.FamilyName} (ناقص {worst.Gap:N0})";
            }
        }

        public bool HasMonthResult => MonthResultText.Length > 0;

        // ═══════════ بحث / فلتر / ترتيب / تجميع ═══════════

        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private PlanFamilyFilter _familyFilter = PlanFamilyFilter.All;
        [ObservableProperty] private PlanFamilySort _familySort = PlanFamilySort.LowestPercent;

        /// <summary>true = أقسام نحاس/زاما زي شيت المصنع، false = كل العيلات في شبكة واحدة</summary>
        [ObservableProperty] private bool _groupByMaterial = true;

        partial void OnSearchTextChanged(string value) => ApplyView();
        partial void OnFamilyFilterChanged(PlanFamilyFilter value) => ApplyView();
        partial void OnFamilySortChanged(PlanFamilySort value) => ApplyView();
        partial void OnGroupByMaterialChanged(bool value) => ApplyView();

        [RelayCommand]
        private void SetFamilyFilter(PlanFamilyFilter filter) => FamilyFilter = FamilyFilter == filter ? PlanFamilyFilter.All : filter;

        public IReadOnlyList<KeyValuePair<PlanFamilySort, string>> SortOptions { get; } = new[]
        {
            new KeyValuePair<PlanFamilySort, string>(PlanFamilySort.LowestPercent, "الأقل نسبة الأول"),
            new KeyValuePair<PlanFamilySort, string>(PlanFamilySort.LargestPlan, "الأكبر خطة الأول"),
            new KeyValuePair<PlanFamilySort, string>(PlanFamilySort.Name, "بالاسم")
        };

        public int BehindFamiliesCount => AllFamilyGroups.Count(g => g.HasPace && g.PaceStatus == PlanPaceStatus.Behind);
        public int OnTrackFamiliesCount => AllFamilyGroups.Count(g => g.HasPace && g.PaceStatus == PlanPaceStatus.OnTrack);
        public int AheadFamiliesCount => AllFamilyGroups.Count(g => g.HasPace && g.PaceStatus == PlanPaceStatus.Ahead);
        public int ForecastBelowFamiliesCount => AllFamilyGroups.Count(g => g.HasForecastBelowPlan);

        public ObservableCollection<MonthlyPlanMaterialView> VisibleMaterialGroups { get; } = new();
        public ObservableCollection<MonthlyPlanFamilyGroupRow> VisibleFamilies { get; } = new();

        public bool HasNoVisibleFamilies => VisibleFamilies.Count == 0 && HasPlanForMonth;

        private bool MatchesFilter(MonthlyPlanFamilyGroupRow g) => FamilyFilter switch
        {
            PlanFamilyFilter.Behind => g.HasPace && g.PaceStatus == PlanPaceStatus.Behind,
            PlanFamilyFilter.OnTrack => g.HasPace && g.PaceStatus == PlanPaceStatus.OnTrack,
            PlanFamilyFilter.Ahead => g.HasPace && g.PaceStatus == PlanPaceStatus.Ahead,
            PlanFamilyFilter.ForecastBelow => g.HasForecastBelowPlan,
            _ => true
        };

        private bool MatchesSearch(MonthlyPlanFamilyGroupRow g)
        {
            var q = SearchText.Trim();
            return q.Length == 0
                   || g.FamilyName.Contains(q, StringComparison.OrdinalIgnoreCase)
                   || g.Products.Any(p => p.ProductName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<MonthlyPlanFamilyGroupRow> Sort(IEnumerable<MonthlyPlanFamilyGroupRow> families) => FamilySort switch
        {
            PlanFamilySort.LargestPlan => families.OrderByDescending(g => g.Subtotal).ThenBy(g => g.FamilyName),
            PlanFamilySort.Name => families.OrderBy(g => g.FamilyName),
            _ => families.OrderBy(g => g.AveragePercent ?? decimal.MaxValue).ThenBy(g => g.FamilyName)
        };

        /// <summary>بيعيد بناء القوايم المعروضة من MaterialGroups — بعد كل تحميل وكل تغيير فلتر</summary>
        private void ApplyView(bool rebuildTable = true)
        {
            VisibleMaterialGroups.Clear();
            VisibleFamilies.Clear();

            foreach (var material in MaterialGroups)
            {
                var families = Sort(material.FamilyGroups.Where(g => MatchesFilter(g) && MatchesSearch(g))).ToList();
                if (families.Count > 0)
                    VisibleMaterialGroups.Add(new MonthlyPlanMaterialView { Material = material, Families = families });
            }
            foreach (var family in Sort(AllFamilyGroups.Where(g => MatchesFilter(g) && MatchesSearch(g))))
                VisibleFamilies.Add(family);

            if (rebuildTable) RebuildTable();

            foreach (var name in new[]
                     {
                         nameof(BehindFamiliesCount), nameof(OnTrackFamiliesCount), nameof(AheadFamiliesCount),
                         nameof(ForecastBelowFamiliesCount), nameof(HasNoVisibleFamilies), nameof(KpiCompletionText),
                         nameof(KpiCompletionColumn), nameof(KpiCompletionRestColumn), nameof(KpiCompletionCaption),
                         nameof(KpiPaceText), nameof(KpiPaceKey), nameof(KpiPaceCaption), nameof(KpiForecastLabel),
                         nameof(KpiForecastText), nameof(KpiForecastCaption), nameof(KpiForecastKey), nameof(IsClosedMonth),
                         nameof(WorkdaysChipText), nameof(KpiRemainingText), nameof(KpiRemainingCaption),
                         nameof(MonthResultText), nameof(HasMonthResult)
                     })
                OnPropertyChanged(name);
        }

        /// <summary>Enter في خانة البحث: لو منتج واحد بالظبط بيطابق، افتح صفحة عيلته على طول</summary>
        [RelayCommand]
        private void OpenSearchResult()
        {
            var q = SearchText.Trim();
            if (q.Length == 0) return;
            var matches = AllFamilyGroups.SelectMany(g => g.Products)
                .Where(p => p.ProductName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) OpenPlanFamilyContaining(matches[0].ProductId);
            else if (VisibleFamilies.Count == 1) SelectedPlanFamily = VisibleFamilies[0];
        }

        // ═══════════ وضع الجدول — إدخال سريع زي الإكسل ═══════════

        [ObservableProperty] private bool _isTableMode;

        /// <summary>في وضع الجدول الحفظ مابيعيدش التحميل (عشان المؤشر مايضيعش بين الخانات) — التحميل الكامل لما يرجع للكروت</summary>
        private bool _tableDirty;

        async partial void OnIsTableModeChanged(bool value)
        {
            if (value)
            {
                SelectedPlanFamily = null;
                RebuildTable();
            }
            else if (_tableDirty)
            {
                _tableDirty = false;
                try { await LoadAsync(); }
                catch (Exception ex) { Notify.Error("تعذّر تحديث الخطة: " + ex.Message); }
            }
        }

        [RelayCommand]
        private void ShowCards() => IsTableMode = false;

        [RelayCommand]
        private void ShowTable() => IsTableMode = true;

        /// <summary>رأس عيلة وبعده منتجاتها — نفس الفلتر والترتيب والبحث بتوع الكروت</summary>
        public ObservableCollection<object> TableItems { get; } = new();

        private void RebuildTable()
        {
            TableItems.Clear();
            if (!IsTableMode) return;
            foreach (var family in VisibleFamilies)
            {
                TableItems.Add(new MonthlyPlanTableHeader
                {
                    Title = $"{family.FamilyName} · {family.MaterialName}",
                    Totals = $"خطة {family.Subtotal:N0} · محقق {family.AchievedSubtotal:N0}"
                });
                foreach (var product in family.Products) TableItems.Add(product);
            }
        }

        /// <summary>
        /// بعد حفظ كمية من وضع الجدول: الإجماليات تتحدّث محليًا من غير إعادة تحميل
        /// (نسب المحقق/التوقّع بتتحسب صح لما يرجع للكروت — _tableDirty)
        /// </summary>
        public void AfterTableEdit()
        {
            _tableDirty = true;
            _inTableEdit = true;
            try { RefreshAggregates(); }
            finally { _inTableEdit = false; }
        }

        private bool _inTableEdit;
    }
}
