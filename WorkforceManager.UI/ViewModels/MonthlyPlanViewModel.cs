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
    /// عقل شاشة "الخطة الشهرية" — دخول الكمية المخططة + تتبّع حي للمحقق
    /// (كل رقم من MonthlyPlanTrackingService، مفيش حساب هنا). خطة/محقق
    /// العيلة دايمًا SUM محسوب، مفيش قيمة بتتكتب عليهم.
    /// </summary>
    public partial class MonthlyPlanViewModel : ObservableObject
    {
        private static readonly string[] ArabicMonthNames =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        /// <summary>أكتر عدد منتجات في "أولوياتي النهاردة"</summary>
        private const int PriorityCount = 5;

        private readonly IServiceScopeFactory _scopeFactory;

        public MonthlyPlanViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            var today = DateTime.Today;
            _selectedYear = today.Year;
            _selectedMonth = today.Month;
            _asOfDate = today;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MonthLabel))]
        [NotifyPropertyChangedFor(nameof(IsCurrentCalendarMonth))]
        [NotifyPropertyChangedFor(nameof(IsPastPeriod))]
        [NotifyPropertyChangedFor(nameof(IsEditable))]
        private int _selectedYear;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MonthLabel))]
        [NotifyPropertyChangedFor(nameof(IsCurrentCalendarMonth))]
        [NotifyPropertyChangedFor(nameof(IsPastPeriod))]
        [NotifyPropertyChangedFor(nameof(IsEditable))]
        private int _selectedMonth;

        public string MonthLabel => $"{ArabicMonthNames[SelectedMonth - 1]} {SelectedYear}";

        /// <summary>الشهر المعروض هو الشهر الحقيقي الحالي؟ — بيحدد افتراضي AsOfDate عند التنقل</summary>
        public bool IsCurrentCalendarMonth => SelectedYear == DateTime.Today.Year && SelectedMonth == DateTime.Today.Month;

        /// <summary>
        /// شهر **فات** فعلاً (قبل الشهر الحالي، مش بعده) — الشهور الجاية
        /// تفضل قابلة للتعديل عن قصد (التخطيط المسبق)، شوف CLAUDE.md.
        /// </summary>
        public bool IsPastPeriod => new DateTime(SelectedYear, SelectedMonth, 1) < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        /// <summary>تبويب "الإنتاج اليومي" مش متأثر بالقيمة دي خالص — هو دايمًا عن النهارده الحقيقي بمعزل عن الشهر المعروض هنا</summary>
        public bool IsEditable => !IsPastPeriod;

        /// <summary>
        /// "لقطة يوم معيّن" (البند 9) — التتبّع بيتحسب لحد التاريخ ده، مش
        /// دايمًا النهارده. بيتظبط تلقائيًا على آخر يوم بالشهر المعروض لو
        /// مش الشهر الحالي (تاريخي بالكامل).
        /// </summary>
        [ObservableProperty] private DateTime _asOfDate;

        [ObservableProperty] private bool _isBusy;

        /// <summary>أيام الشغل الكلية/المتبقية للفترة المعروضة — محسوبة فعليًا (WorkCalendarRules)، مش رقم ثابت</summary>
        [ObservableProperty] private int _totalWorkdays;
        [ObservableProperty] private int _remainingWorkdays;

        /// <summary>"آخر تحديث للمحقق" — فاضي لو لسه مفيش إدخال يدوي خالص الشهر ده</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLastUpdated))]
        private string _lastUpdatedText = "";

        public bool HasLastUpdated => LastUpdatedText.Length > 0;

        /// <summary>كجم/طن لعرض إجمالي الوزن — الأرقام المخزّنة نفسها (Grams) ثابتة، ده عرض بس</summary>
        [ObservableProperty] private bool _showWeightInTons;

        [RelayCommand]
        private void ToggleWeightUnit() => ShowWeightInTons = !ShowWeightInTons;

        /// <summary>
        /// مستوى تجميع أعلى من العيلة — مادة (نحاس/زاما/غير محدد)، زي قسمين
        /// منفصلين في شيت المصنع الأصلي. كل قسم بيحمل عائلاته وإجمالياته.
        /// </summary>
        public ObservableCollection<MonthlyPlanMaterialGroupRow> MaterialGroups { get; } = new();

        /// <summary>
        /// منتجات عندها إنتاج بس مفيش خطة مسجّلة — قسم مستقل تمامًا عن
        /// تجميع المادة/العيلة (مش شارة داخلية زي قبل كده)، عشان مفيش خطة
        /// تتقاس بيها أصلاً (مفيش نسبة/بار تقدم لها).
        /// </summary>
        public ObservableCollection<MonthlyPlanProductRow> OutsidePlanProducts { get; } = new();

        public bool HasOutsidePlanProducts => OutsidePlanProducts.Count > 0;

        /// <summary>أوقات اللقطات المحفوظة للشهر المعروض — حفظ + قايمة بس، شوف CLAUDE.md</summary>
        public ObservableCollection<DateTime> SnapshotTimestamps { get; } = new();

        public bool HasSnapshots => SnapshotTimestamps.Count > 0;

        private async Task LoadSnapshotTimestampsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var timestamps = await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                .GetSnapshotTimestampsAsync(SelectedYear, SelectedMonth);

            SnapshotTimestamps.Clear();
            foreach (var t in timestamps) SnapshotTimestamps.Add(t);
            OnPropertyChanged(nameof(HasSnapshots));
        }

        [RelayCommand]
        private async Task SaveSnapshotAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                .SaveSnapshotAsync(SelectedYear, SelectedMonth, AsOfDate);

            await LoadSnapshotTimestampsAsync();
            Notify.Info("اتحفظت لقطة لحالة المحقق دلوقتي", "لقطة نهاية اليوم");
        }

        public async Task<List<MonthlyPlanSnapshotRowDto>> LoadSnapshotAsync(DateTime takenAt)
        {
            using var scope = _scopeFactory.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                .GetSnapshotAsync(takenAt);
        }

        /// <summary>أكتر 3-5 منتجات محتاجة دفعة النهارده — الأبعد عن خطتها بين اللي متأخرين</summary>
        public ObservableCollection<MonthlyPlanProductRow> TodaysPriorities { get; } = new();

        /// <summary>كل مجموعات العيلة عبر مجموعات المادة كلها — مسطّحة، للحسابات الإجمالية ولإيجاد المجموعة المالكة لصف معيّن من الكود الخلفي</summary>
        public IEnumerable<MonthlyPlanFamilyGroupRow> AllFamilyGroups => MaterialGroups.SelectMany(m => m.FamilyGroups);

        public int GrandTotalPlan => MaterialGroups.Sum(m => m.Subtotal);
        public int GrandTotalAchieved => MaterialGroups.Sum(m => m.AchievedSubtotal);

        // ------- شريط الملخص (البند 8) -------

        [ObservableProperty] private int _onTrackCount;
        [ObservableProperty] private int _behindCount;
        [ObservableProperty] private int _aheadCount;

        /// <summary>أول 3 عائلات واطية عن الإيقاع — للتنبيه اللطيف (البند 6)</summary>
        public ObservableCollection<string> BelowThresholdFamilyNames { get; } = new();

        /// <summary>نص جاهز للعرض المباشر — StringFormat على الـCollection نفسها بيطبع اسم النوع مش محتواها</summary>
        public string BelowThresholdFamiliesText =>
            $"عائلة تحت 75% من إيقاعها المتوقع: {string.Join("، ", BelowThresholdFamilyNames)}";

        private void RefreshAggregates()
        {
            OnPropertyChanged(nameof(GrandTotalPlan));
            OnPropertyChanged(nameof(GrandTotalAchieved));

            var allProducts = AllFamilyGroups.SelectMany(g => g.Products).Where(p => !p.IsOutsidePlan || p.Quantity > 0).ToList();
            OnTrackCount = allProducts.Count(p => p.Status == PlanPaceStatus.OnTrack);
            BehindCount = allProducts.Count(p => p.Status == PlanPaceStatus.Behind);
            AheadCount = allProducts.Count(p => p.Status == PlanPaceStatus.Ahead);

            BelowThresholdFamilyNames.Clear();
            foreach (var name in AllFamilyGroups.Where(g => g.IsBelowThreshold).Select(g => g.HeaderText))
                BelowThresholdFamilyNames.Add(name);
            OnPropertyChanged(nameof(HasBelowThresholdFamilies));
            OnPropertyChanged(nameof(BelowThresholdFamiliesText));

            TodaysPriorities.Clear();
            // الأبعد عن الخطة الأول — الفجوة المطلقة بين المطلوب يوميًا والمُنجز النهارده فعليًا
            foreach (var p in allProducts
                         .Where(p => p.Status == PlanPaceStatus.Behind && p.RequiredDailyOutput is not null)
                         .OrderByDescending(p => (p.RequiredDailyOutput ?? 0) - p.TodayCompleted)
                         .Take(PriorityCount))
                TodaysPriorities.Add(p);
            OnPropertyChanged(nameof(HasPriorities));
        }

        public bool HasBelowThresholdFamilies => BelowThresholdFamilyNames.Count > 0;
        public bool HasPriorities => TodaysPriorities.Count > 0;

        [RelayCommand]
        private async Task PreviousMonthAsync()
        {
            (SelectedYear, SelectedMonth) = SelectedMonth == 1 ? (SelectedYear - 1, 12) : (SelectedYear, SelectedMonth - 1);
            ResetAsOfDateForSelectedMonth();
            await LoadAsync();
        }

        [RelayCommand]
        private async Task NextMonthAsync()
        {
            (SelectedYear, SelectedMonth) = SelectedMonth == 12 ? (SelectedYear + 1, 1) : (SelectedYear, SelectedMonth + 1);
            ResetAsOfDateForSelectedMonth();
            await LoadAsync();
        }

        /// <summary>الشهر الحالي → النهارده. شهر تاريخي (فات) → آخر يوم فيه (لقطة كاملة الشهر)</summary>
        private void ResetAsOfDateForSelectedMonth() => AsOfDate = IsCurrentCalendarMonth
            ? DateTime.Today
            : new DateTime(SelectedYear, SelectedMonth, DateTime.DaysInMonth(SelectedYear, SelectedMonth));

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var trackingService = scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>();
                var tracking = await trackingService.GetTrackingAsync(SelectedYear, SelectedMonth, AsOfDate);

                var remainingWorkdays = tracking.Count > 0 ? tracking[0].RemainingWorkdays : 0;
                TotalWorkdays = tracking.Count > 0 ? tracking[0].TotalWorkdays : 0;
                RemainingWorkdays = remainingWorkdays;

                var lastUpdated = await trackingService.GetLastUpdatedAsync(SelectedYear, SelectedMonth);
                LastUpdatedText = lastUpdated is { } updated ? $"آخر تحديث: الساعة {updated:HH:mm}" : "";

                MaterialGroups.Clear();

                OutsidePlanProducts.Clear();
                foreach (var row in tracking.Where(p => p.IsOutsidePlan).OrderBy(p => p.ProductName).Select(ToRow))
                    OutsidePlanProducts.Add(row);
                OnPropertyChanged(nameof(HasOutsidePlanProducts));

                var inPlan = tracking.Where(p => !p.IsOutsidePlan).ToList();

                // مادة فوق عيلة — نفس ترتيب شيت المصنع الأصلي (قسم نحاس كامل، قسم زاما كامل)
                foreach (var materialGroup in inPlan
                             .GroupBy(p => p.Material)
                             .OrderBy(g => g.Key switch { Core.Enums.Material.Copper => 0, Core.Enums.Material.Zamak => 1, _ => 2 }))
                {
                    var header = materialGroup.Key switch
                    {
                        Core.Enums.Material.Copper => "نحاس",
                        Core.Enums.Material.Zamak => "زاما",
                        _ => "غير محدد"
                    };
                    MaterialGroups.Add(new MonthlyPlanMaterialGroupRow
                    {
                        HeaderText = header, FamilyGroups = BuildFamilyGroups(materialGroup, remainingWorkdays)
                    });
                }

                RefreshAggregates();
                await LoadSnapshotTimestampsAsync();
            }
            finally { IsBusy = false; }
        }

        private static List<MonthlyPlanFamilyGroupRow> BuildFamilyGroups(IEnumerable<MonthlyPlanTrackingDto> products, int remainingWorkdays)
        {
            var groups = new List<MonthlyPlanFamilyGroupRow>();

            foreach (var g in products
                         .Where(p => p.FamilyId is not null)
                         .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                         .OrderBy(g => g.Key.Item2))
            {
                groups.Add(new MonthlyPlanFamilyGroupRow
                {
                    HeaderText = $"{g.Key.Item2} ({g.Count()})",
                    Products = OrderByPace(g).Select(ToRow).ToList(),
                    RemainingWorkdays = remainingWorkdays
                });
            }

            var noFamily = products.Where(p => p.FamilyId is null).ToList();
            if (noFamily.Count > 0)
                groups.Add(new MonthlyPlanFamilyGroupRow
                {
                    HeaderText = $"بدون عيلة ({noFamily.Count})", Products = OrderByPace(noFamily).Select(ToRow).ToList(),
                    RemainingWorkdays = remainingWorkdays
                });

            return groups;
        }

        /// <summary>المتأخر يطلع فوق (البند 4) — Behind أولاً، بعدين الأقل نسبة، بعدين الاسم</summary>
        private static IEnumerable<MonthlyPlanTrackingDto> OrderByPace(IEnumerable<MonthlyPlanTrackingDto> products) =>
            products
                .OrderBy(p => p.Status == PlanPaceStatus.Behind ? 0 : p.Status == PlanPaceStatus.OnTrack ? 1 : 2)
                .ThenBy(p => p.AchievedPercent ?? 1m)
                .ThenBy(p => p.ProductName);

        private static MonthlyPlanProductRow ToRow(MonthlyPlanTrackingDto p) => new()
        {
            ProductId = p.ProductId, ProductName = p.ProductName, IsComplete = p.IsComplete,
            QuantityText = p.PlannedQuantity.ToString(),
            AchievedToDate = p.AchievedToDate, TodayCompleted = p.TodayCompleted,
            EffectiveAchieved = p.EffectiveAchieved, ProRatedPlan = p.ProRatedPlan,
            AchievedPercent = p.AchievedPercent, Status = p.Status,
            RequiredDailyOutput = p.RequiredDailyOutput, ForecastEndOfMonth = p.ForecastEndOfMonth,
            SameDayPreviousMonth = p.SameDayPreviousMonth, IsOutsidePlan = p.IsOutsidePlan,
            CorrectionText = p.CorrectionsToDate.ToString(),
            DailyTargetText = p.DailyTargetQuantity?.ToString() ?? "",
            TotalWeightGrams = p.TotalWeightGrams,
            HasReachedTarget = p.HasReachedTarget
        };

        public async Task SaveQuantityAsync(int productId, int quantity)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                .SetPlanAsync(productId, SelectedYear, SelectedMonth, quantity);
        }

        /// <summary>"الخطة اليومية" — هدف يومي يدوي، null بيشيله (مش صفر)</summary>
        public async Task SaveDailyTargetAsync(int productId, int? dailyTarget)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                .SetDailyTargetAsync(productId, SelectedYear, SelectedMonth, dailyTarget);
        }

        // SaveCorrectionAsync اتشالت من هنا — إدخال التصليحات اتنقل لشاشة
        // تسجيل الإنتاج اليومي (FlowSessionViewModel.SaveCorrectionAsync)،
        // شوف CLAUDE.md. الشاشة دي لسه بتعرض CorrectionsToDate/CorrectionsSubtotal
        // للقراءة بس.

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

        /// <summary>
        /// تصدير إكسل — نفس بيانات الشاشة المعروضة بالظبط (نفس الشهر ونفس
        /// AsOfDate)، مش استعلام تاني منفصل. طقوس الحفظ/الفتح/الأخطاء
        /// مشتركة (ExcelExport)، زي أي تصدير تاني في البرنامج.
        /// </summary>
        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task ExportExcelAsync()
        {
            await ExcelExport.RunAsync(
                "تصدير الخطة الشهرية", $"الخطة الشهرية {MonthLabel}",
                async filePath =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var tracking = await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                        .GetTrackingAsync(SelectedYear, SelectedMonth, AsOfDate);

                    scope.ServiceProvider.GetRequiredService<MonthlyPlanExcelService>()
                        .Export(tracking, $"{MonthLabel} — لحد {AsOfDate:yyyy/MM/dd}", filePath);
                });
        }

        // ═══════════ تبويب "الإنتاج اليومي" (إدخال يدوي، بديل المحقق
        // التلقائي) — دايمًا عن النهارده الحقيقي، مستقل عن SelectedYear/
        // SelectedMonth فوق. شوف CLAUDE.md. ═══════════

        public DateTime TodayEntryDate { get; } = DateTime.Today;

        public ObservableCollection<MonthlyPlanTodayEntryRow> TodayEntryRows { get; } = new();

        /// <summary>غير المُدخل يطلع فوق (قاعدة هندسية صريحة من الطلب) — بمعزل تام عن أي حالة إنتاج حقيقي</summary>
        public static List<MonthlyPlanTodayEntryRow> OrderTodayEntryRows(IEnumerable<MonthlyPlanTodayEntryRow> rows) =>
            rows.OrderBy(r => r.IsFilled).ThenBy(r => r.ProductName).ToList();

        public async Task LoadTodayEntryTabAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dtos = await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                .GetTodayEntryTabAsync(TodayEntryDate);

            TodayEntryRows.Clear();
            foreach (var row in OrderTodayEntryRows(dtos.Select(MonthlyPlanTodayEntryRow.FromDto)))
                TodayEntryRows.Add(row);
        }

        public async Task SaveTodayEntryAsync(int productId, int quantity)
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonthlyPlanTrackingService>()
                .SetDailyEntryAsync(productId, TodayEntryDate, quantity);

            await LoadTodayEntryTabAsync();
        }

        /// <summary>"استخدم نفس الرقم الحقيقي" — نسخة مرة واحدة وقت الضغط، مش ربط حي، وبتتحفظ فورًا</summary>
        [RelayCommand]
        private async Task UseRealProductionAsync(MonthlyPlanTodayEntryRow row)
        {
            row.QuantityText = row.RealProductionToday.ToString();
            await SaveTodayEntryAsync(row.ProductId, row.RealProductionToday);
        }

        /// <summary>"انسخ إنتاج أمبارح" — نفس فكرة الزرار فوق، من قيمة أمبارح المُدخلة يدويًا</summary>
        [RelayCommand]
        private async Task CopyYesterdayAsync(MonthlyPlanTodayEntryRow row)
        {
            row.QuantityText = row.YesterdayQuantity.ToString();
            await SaveTodayEntryAsync(row.ProductId, row.YesterdayQuantity);
        }
    }
}
