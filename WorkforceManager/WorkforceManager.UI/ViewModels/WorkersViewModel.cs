using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة العمال: تحميل القائمة بإحصائيات الأسبوع الحالي، البحث
    /// الموحّد (بالاسم أو بالمهارة)، ولوحة تفاصيل العامل المحدد (مهارات +
    /// هستوري أسبوعي + جزاءات)، وأوامر الإضافة/التعديل/الإيقاف.
    ///
    /// بنعمل Scope جديد لكل عملية (بدل حقن الخدمات مباشرة) عشان الـ
    /// DbContext يفضل قصير العمر — قاعدة أساسية لتفادي مشاكل التتبع
    /// والذاكرة في تطبيقات سطح المكتب طويلة التشغيل.
    /// </summary>
    public partial class WorkersViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public WorkersViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            _selectedSort = SortOptions[0]; // الترتيب الافتراضي: بالاسم
        }

        // ------- حالة الشاشة -------

        /// <summary>
        /// كل العمال محمّلين مرة واحدة (نشطين وموقوفين). البحث والفلاتر
        /// والترتيب بيشتغلوا على القائمة دي في الذاكرة — عشان كده الشاشة
        /// بتستجيب فورًا مع كل حرف من غير أي انتظار.
        /// </summary>
        private readonly List<WorkerRow> _allWorkers = new();

        /// <summary>
        /// إعادة تحميل القائمة شغالة دلوقتي — التحديد اللي بيتصفّر جوّاها
        /// لحظي ومش خروج من البروفايل. شوف <see cref="OnSelectedWorkerChanged"/>.
        /// </summary>
        private bool _reloadingRows;

        /// <summary>
        /// فيه مهارات اتغيّرت والقائمة لسه ما اتحدّثتش.
        ///
        /// وهو في وضع الإضافة المستخدم بيضيف عشر مهارات ورا بعض، وكل
        /// إعادة تحميل بتحرّك الصفوف تحت إيده. فالتحديث بيتأجّل لحد ما
        /// يدوس "خلصت" — وقتها بيتعمل مرة واحدة. المهارة نفسها بتتسجّل
        /// في الداتابيز فورًا، المؤجّل هو عدّاد الكارت في القائمة بس.
        /// </summary>
        private bool _skillRowsStale;

        /// <summary>نص البحث الموحّد: اسم عامل أو اسم مرحلة/منتج</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) => ApplyFilters();

        /// <summary>الفلتر السريع المختار (الكل / بالإنتاج / بالساعة / موقوفين)</summary>
        [ObservableProperty]
        private WorkerFilter _activeFilter = WorkerFilter.All;

        partial void OnActiveFilterChanged(WorkerFilter value)
        {
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterPiece));
            OnPropertyChanged(nameof(IsFilterHourly));
            OnPropertyChanged(nameof(IsFilterInactive));
            ApplyFilters();
        }

        // الخصائص دي بتربط أزرار الفلترة بالحالة الحالية (الزرار المفعّل بيتلون)
        public bool IsFilterAll => ActiveFilter == WorkerFilter.All;
        public bool IsFilterPiece => ActiveFilter == WorkerFilter.PieceRate;
        public bool IsFilterHourly => ActiveFilter == WorkerFilter.Hourly;
        public bool IsFilterInactive => ActiveFilter == WorkerFilter.Inactive;

        [RelayCommand]
        private void SetFilter(string? filter) =>
            ActiveFilter = filter switch
            {
                "piece" => WorkerFilter.PieceRate,
                "hourly" => WorkerFilter.Hourly,
                "inactive" => WorkerFilter.Inactive,
                _ => WorkerFilter.All
            };

        // ------- الفلاتر المركّبة (بتتجمع مع الشريحة بـ AND) -------

        /// <summary>فلترة بمرحلة العامل مؤهل ليها (null = كل المراحل)</summary>
        [ObservableProperty]
        private StageFilterOption? _selectedStageFilter;

        partial void OnSelectedStageFilterChanged(StageFilterOption? value) => ApplyFilters();

        /// <summary>فلترة بمنتج عنده مهارة في أي مرحلة منه</summary>
        [ObservableProperty]
        private ProductFilterOption? _selectedProductFilter;

        partial void OnSelectedProductFilterChanged(ProductFilterOption? value) => ApplyFilters();

        /// <summary>أقل متوسط نجوم مقبول (null = أي تقييم)</summary>
        [ObservableProperty]
        private StarsFilterOption? _selectedStarsFilter;

        partial void OnSelectedStarsFilterChanged(StarsFilterOption? value) => ApplyFilters();

        /// <summary>حالة الحضور النهارده</summary>
        [ObservableProperty]
        private AttendanceFilterOption? _selectedAttendanceFilter;

        partial void OnSelectedAttendanceFilterChanged(AttendanceFilterOption? value) => ApplyFilters();

        /// <summary>المراحل المتاحة للفلترة (كل مراحل المصنع النشطة)</summary>
        public ObservableCollection<StageFilterOption> StageFilterOptions { get; } = new();

        /// <summary>المنتجات المتاحة للفلترة</summary>
        public ObservableCollection<ProductFilterOption> ProductFilterOptions { get; } = new();

        public List<StarsFilterOption> StarsFilterOptions { get; } = new()
        {
            new(null, "أي تقييم"),
            new(5, "★★★★★ ممتاز"),
            new(4, "★★★★ فأكتر"),
            new(3, "★★★ فأكتر"),
            new(2, "★★ فأكتر")
        };

        public List<AttendanceFilterOption> AttendanceFilterOptions { get; } = new()
        {
            new(null, "أي حالة حضور"),
            new(AttendanceStatus.Present, "حاضر النهارده"),
            new(AttendanceStatus.AbsentWithPermission, "غايب بإذن"),
            new(AttendanceStatus.AbsentWithoutPermission, "غايب من غير إذن")
        };

        /// <summary>فيه فلتر مركّب مفعّل؟ (بيظهر زرار "شيل الفلاتر")</summary>
        /// <summary>لوحة الفلاتر مفتوحة</summary>
        [ObservableProperty]
        private bool _isFilterMenuOpen;

        /// <summary>
        /// كام فلتر شغّال دلوقتي — بيتعرض كرقم على زرار الفلاتر.
        ///
        /// الفلاتر بقت جوّه لوحة، فمن غير الرقم ده المستخدم ممكن يبص على
        /// قايمة مفلترة ومايعرفش ليه ناقصة.
        /// </summary>
        public int ActiveFilterCount =>
            (SelectedStageFilter?.StageId is not null ? 1 : 0) +
            (SelectedProductFilter?.ProductId is not null ? 1 : 0) +
            (SelectedStarsFilter?.MinStars is not null ? 1 : 0) +
            (SelectedAttendanceFilter?.Status is not null ? 1 : 0);

        public bool HasExtraFilters => ActiveFilterCount > 0;

        /// <summary>يجمّع الفلاتر المختارة في معايير واحدة للقاعدة</summary>
        private WorkerFilterCriteria BuildCriteria() => new()
        {
            Scope = ActiveFilter switch
            {
                WorkerFilter.PieceRate => WorkerPayScope.ByProduction,
                WorkerFilter.Hourly => WorkerPayScope.Hourly,
                WorkerFilter.Inactive => WorkerPayScope.Inactive,
                _ => WorkerPayScope.AllActive
            },
            StageId = SelectedStageFilter?.StageId,
            ProductId = SelectedProductFilter?.ProductId,
            MinStars = SelectedStarsFilter?.MinStars,
            TodayStatus = SelectedAttendanceFilter?.Status
        };

        /// <summary>يرجّع الفلاتر المركّبة لوضعها الافتراضي</summary>
        [RelayCommand]
        private void ClearExtraFilters()
        {
            SelectedStageFilter = StageFilterOptions.FirstOrDefault();
            SelectedProductFilter = ProductFilterOptions.FirstOrDefault();
            SelectedStarsFilter = StarsFilterOptions[0];
            SelectedAttendanceFilter = AttendanceFilterOptions[0];
        }

        /// <summary>طريقة الترتيب المختارة</summary>
        [ObservableProperty]
        private WorkerSortOption? _selectedSort;

        partial void OnSelectedSortChanged(WorkerSortOption? value) => ApplyFilters();

        public List<WorkerSortOption> SortOptions { get; } = new()
        {
            new(WorkerSort.Name, "الاسم (أ ← ي)"),
            new(WorkerSort.NetDesc, "الأعلى صافي يوميات"),
            new(WorkerSort.NetAsc, "الأقل صافي يوميات"),
            new(WorkerSort.AbsenceDesc, "الأكتر غيابًا"),
            new(WorkerSort.SkillsDesc, "الأكتر مهارات")
        };

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private WorkerRow? _selectedWorker;

        /// <summary>تفاصيل العامل المحدد (بتتحمّل لحظة اختياره من القائمة)</summary>
        [ObservableProperty]
        private WorkerDetail? _detail;

        /// <summary>العمال المعروضين دلوقتي بعد البحث والفلترة والترتيب</summary>
        public ObservableCollection<WorkerRow> Workers { get; } = new();

        /// <summary>عنوان الأسبوع الحالي المعروض فوق القائمة (من الخميس للأربع)</summary>
        [ObservableProperty]
        private string _weekTitle = string.Empty;

        // ------- عدّادات الملخص -------

        public int TotalCount => _allWorkers.Count;
        public int ActiveCount => _allWorkers.Count(w => w.IsActive);
        public int InactiveCount => _allWorkers.Count(w => !w.IsActive);
        public int HourlyCount => _allWorkers.Count(w => w.IsActive && w.IsHourly);

        /// <summary>عدد العمال اللي محتاجين انتباه (مفيش سعر يومية أو مفيش مهارات)</summary>
        public int NeedsAttentionCount => _allWorkers.Count(w => w.IsActive && w.NeedsAttention);

        /// <summary>
        /// أحسن عامل في الأسبوع. **مين هو** بيتحدد في WeeklySummaryService
        /// (أعلى صافي يوميات، بشرط إنه أنتج وصافيه موجب) — الشاشة بتعرض
        /// النتيجة بس ومش بتحسبها.
        /// </summary>
        public WorkerRow? BestWorker => _allWorkers.FirstOrDefault(w => w.IsBestOfWeek);

        public bool HasBestWorker => BestWorker is not null;

        public string BestWorkerName => BestWorker?.FullName ?? "—";

        /// <summary>يفتح بروفايل أحسن عامل — الكارت كله زرار</summary>
        [RelayCommand]
        private void OpenBestWorker()
        {
            var best = BestWorker;
            if (best is null) return;

            // ممكن يكون مخفي تحت فلتر شغال دلوقتي، فبنرجّع القايمة
            // لوضعها الطبيعي الأول عشان الاختيار يبان فعلاً
            if (!Workers.Contains(best))
            {
                SearchText = string.Empty;
                ActiveFilter = WorkerFilter.All;
                ClearExtraFilters();
            }

            SelectedWorker = best;
        }

        /// <summary>عدد النتايج المعروضة دلوقتي (بيظهر جنب البحث)</summary>
        public string ResultsText => Workers.Count == TotalCount
            ? $"{Workers.Count} عامل"
            : $"{Workers.Count} من {TotalCount}";

        public bool NoResults => Workers.Count == 0 && _allWorkers.Count > 0;

        private void RefreshSummary()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(InactiveCount));
            OnPropertyChanged(nameof(HourlyCount));
            OnPropertyChanged(nameof(NeedsAttentionCount));
            OnPropertyChanged(nameof(BestWorker));
            OnPropertyChanged(nameof(HasBestWorker));
            OnPropertyChanged(nameof(BestWorkerName));
            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
        }

        /// <summary>
        /// بيطبّق البحث + الفلتر + الترتيب على القائمة المحمّلة. كله في
        /// الذاكرة، فبيتنادى مع كل حرف من غير أي تكلفة.
        /// </summary>
        private void ApplyFilters()
        {
            var query = SearchText?.Trim() ?? "";

            // كل الفلاتر (الشريحة + المرحلة + النجوم + الحضور + المنتج)
            // بتتطبّق مع بعض بـ AND في WorkerFilterRules — القاعدة عايشة
            // في طبقة الأعمال عشان تتختبر من غير واجهة
            IEnumerable<WorkerRow> result =
                WorkerFilterRules.Apply(_allWorkers, w => w.FilterSubject, BuildCriteria());

            // البحث بيشمل الاسم والمهارات (اسم المرحلة أو المنتج) والملاحظات.
            // بيتجاهل الهمزات زي بحث العمال في شاشة التسجيل اليومي — لو بحث
            // واحد بيلاقي "احمد" والتاني لأ، ده بيبان للمستخدم كعطل مش كفرق
            if (query.Length > 0)
                result = result.Where(w =>
                    ArabicSearch.Contains(w.FullName, query) ||
                    ArabicSearch.Contains(w.SkillsSearchText, query));

            result = (SelectedSort?.Value ?? WorkerSort.Name) switch
            {
                WorkerSort.NetDesc => result.OrderByDescending(w => w.NetWorkdays).ThenBy(w => w.FullName),
                WorkerSort.NetAsc => result.OrderBy(w => w.NetWorkdays).ThenBy(w => w.FullName),
                WorkerSort.AbsenceDesc => result
                    .OrderByDescending(w => w.AbsentWithoutPermissionDays + w.AbsentWithPermissionDays)
                    .ThenBy(w => w.FullName),
                WorkerSort.SkillsDesc => result.OrderByDescending(w => w.SkillsCount).ThenBy(w => w.FullName),
                _ => result.OrderBy(w => w.FullName)
            };

            Workers.Clear();
            foreach (var row in result) Workers.Add(row);

            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
            OnPropertyChanged(nameof(HasExtraFilters));
            OnPropertyChanged(nameof(ActiveFilterCount));
        }

        /// <summary>يمسح البحث ويرجّع كل الفلاتر للكل</summary>
        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
            ActiveFilter = WorkerFilter.All;
            ClearExtraFilters();
        }

        /// <summary>يعرض العمال المحتاجين انتباه بس (من زرار التنبيه في الملخص)</summary>
        [RelayCommand]
        private void ShowNeedsAttention()
        {
            SearchText = string.Empty;
            ActiveFilter = WorkerFilter.All;

            Workers.Clear();
            foreach (var row in _allWorkers.Where(w => w.IsActive && w.NeedsAttention).OrderBy(w => w.FullName))
                Workers.Add(row);

            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
        }

        // لما العامل المحدد يتغير، حمّل تفاصيله في اللوحة الجانبية
        partial void OnSelectedWorkerChanged(WorkerRow? value)
        {
            // إعادة تحميل القائمة بتنادي Workers.Clear()، وWPF بيصفّر
            // التحديد أول ما الصف يتشال. ده مش خروج من البروفايل — الصف
            // بيترجع بعدها بسطرين. من غير الحارس ده كان Detail بيتمسح في
            // اللحظة دي، فلقطة حالة اللوحة (الكارت المفتوح + وضع الإضافة)
            // بتضيع واللوحة بتتبني من الأول مقفولة.
            if (_reloadingRows && value is null) return;

            // تحميل بروفايل العامل المحدد (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(() => LoadDetailAsync(value));
        }

        // ------- تحميل القائمة -------

        /// <summary>
        /// يملا قوايم المراحل والمنتجات المتاحة للفلترة.
        ///
        /// المراحل الموقوفة مستبعدة: الفلترة بيها بتدّي عمال مش هيشتغلوا
        /// عليها أصلاً. الاختيار الحالي بيتحافظ عليه لو لسه موجود، عشان
        /// إعادة التحميل بعد أي تعديل متلغيش فلتر المستخدم.
        /// </summary>
        private async Task LoadFilterOptionsAsync(IProductRepository productRepo)
        {
            var products = await productRepo.GetAllWithStagesAsync();

            var previousStageId = SelectedStageFilter?.StageId;
            var previousProductId = SelectedProductFilter?.ProductId;

            StageFilterOptions.Clear();
            StageFilterOptions.Add(new StageFilterOption(null, "كل المراحل"));

            ProductFilterOptions.Clear();
            ProductFilterOptions.Add(new ProductFilterOption(null, "كل المنتجات"));

            foreach (var product in products.Where(p => p.IsActive).OrderBy(p => p.Name))
            {
                ProductFilterOptions.Add(new ProductFilterOption(product.Id, product.Name));

                foreach (var stage in product.Stages
                             .Where(s => s.IsActive)
                             .OrderBy(s => s.SortOrder).ThenBy(s => s.Id))
                    StageFilterOptions.Add(
                        new StageFilterOption(stage.Id, $"{product.Name} — {stage.StageName}"));
            }

            SelectedStageFilter = StageFilterOptions.FirstOrDefault(o => o.StageId == previousStageId)
                                  ?? StageFilterOptions[0];
            SelectedProductFilter = ProductFilterOptions.FirstOrDefault(o => o.ProductId == previousProductId)
                                    ?? ProductFilterOptions[0];
            SelectedStarsFilter ??= StarsFilterOptions[0];
            SelectedAttendanceFilter ??= AttendanceFilterOptions[0];
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();
                var recognitionService = scope.ServiceProvider.GetRequiredService<WorkerRecognitionService>();
                var trendService = scope.ServiceProvider.GetRequiredService<ProductionTrendService>();
                var attendanceRepo = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
                var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();

                var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);
                WeekTitle = $"الأسبوع الحالي: من الخميس {weekStart:yyyy/MM/dd} إلى الأربعاء {weekEnd:yyyy/MM/dd}";

                // إحصائيات الأسبوع الحالي لكل العمال (استعلام واحد مجمّع)
                var weekly = await weeklyService.GetTeamWeeklySummaryAsync(DateTime.Today);
                var weeklyByWorker = weekly.ToDictionary(w => w.WorkerId);

                // الألقاب الرسمية الحالية (أسبوعي حد 3 عمال + شهري عامل واحد) —
                // شارات ثابتة على البروفايل، منفصلة عن IsBestOfWeek اللحظي فوق
                var currentTitles = await recognitionService.GetCurrentTitleHoldersAsync();
                var weeklyTitleByWorker = currentTitles
                    .Where(t => t.TitleType == PerformanceTitleType.WeeklyTop3)
                    .ToDictionary(t => t.WorkerId, t => $"🏆 أحسن عامل الأسبوع (من {t.PeriodStart:d MMMM})");
                var monthlyTitleByWorker = currentTitles
                    .Where(t => t.TitleType == PerformanceTitleType.MonthlyBest)
                    .ToDictionary(t => t.WorkerId, t => $"🏆 أحسن عامل الشهر ({t.PeriodStart:MMMM})");

                // عمال إنتاجهم النهارده قلّ بشكل ملحوظ عن متوسطهم — علامة "يحتاج مراجعة"
                var decliningByWorker = (await trendService.GetDecliningWorkersAsync(DateTime.Today))
                    .ToDictionary(d => d.WorkerId);

                // حضور النهارده لفلتر الحضور — استعلام واحد لليوم كله
                var todayAttendance = (await attendanceRepo.GetByDateAsync(DateTime.Today))
                    .ToDictionary(a => a.WorkerId, a => a.Status);

                // كل العمال بمهاراتهم مرة واحدة — الفلترة والبحث بعد كده في الذاكرة
                var workers = await workerRepo.GetAllWithSkillsAsync();

                await LoadFilterOptionsAsync(productRepo);

                _allWorkers.Clear();
                foreach (var w in workers)
                {
                    weeklyByWorker.TryGetValue(w.Id, out var wk);

                    var skillNames = w.Skills
                        .Select(s => $"{s.ProductionStage.Product?.Name} {s.ProductionStage.StageName}")
                        .ToList();

                    _allWorkers.Add(new WorkerRow
                    {
                        WorkerId = w.Id,
                        FullName = w.FullName,
                        IsActive = w.IsActive,
                        HourlyRoleText = w.HourlyRole?.ToArabicName() ?? "",
                        IsHourly = w.IsHourly,
                        DailyWageEgp = w.DailyWageEgp,
                        SkillsCount = w.Skills.Count,
                        PresentDays = wk?.PresentDays ?? 0,
                        AbsentWithPermissionDays = wk?.AbsentWithPermissionDays ?? 0,
                        AbsentWithoutPermissionDays = wk?.AbsentWithoutPermissionDays ?? 0,
                        PenaltyDeduction = wk?.PenaltyDeduction ?? 0,
                        NetWorkdays = wk?.NetWorkdays ?? 0,
                        IsBestOfWeek = wk?.IsBestWorkerOfWeek == true,
                        PhotoData = w.PhotoData,
                        // بيانات الفلاتر المركّبة
                        StageIds = w.Skills.Select(s => s.ProductionStageId).ToHashSet(),
                        ProductIds = w.Skills
                            .Select(s => s.ProductionStage.ProductId)
                            .ToHashSet(),
                        AverageStars = w.Skills.Count == 0
                            ? 0m
                            : Math.Round((decimal)w.Skills.Average(s => s.Stars), 2),
                        TodayStatus = todayAttendance.TryGetValue(w.Id, out var status) ? status : null,
                        // المنتج اللي متوسط نجومه فيه الأعلى — شارة كارت أحسن عامل
                        TopSkillProduct = w.Skills
                            .Where(s => s.ProductionStage.Product is not null)
                            .GroupBy(s => s.ProductionStage.Product!.Name)
                            .OrderByDescending(g => g.Average(s => s.Stars))
                            .ThenBy(g => g.Key)
                            .Select(g => g.Key)
                            .FirstOrDefault() ?? "",
                        // متوسط الأداء الفعلي (إنتاج ÷ يومية) على مهاراته المقاسة
                        // بس — مهارة عمرها ما اتقاست (MeasuredAt == null) مالهاش رقم
                        AverageMeasuredRatio = w.Skills.Any(s => s.MeasuredAt != null)
                            ? Math.Round(w.Skills.Where(s => s.MeasuredAt != null).Average(s => s.MeasuredRatio), 2)
                            : null,
                        CurrentWeeklyTitleText = weeklyTitleByWorker.GetValueOrDefault(w.Id, ""),
                        CurrentMonthlyTitleText = monthlyTitleByWorker.GetValueOrDefault(w.Id, ""),
                        NeedsProductionReview = decliningByWorker.ContainsKey(w.Id),
                        ProductionReviewText = decliningByWorker.TryGetValue(w.Id, out var decline)
                            ? $"إنتاجه النهارده {decline.PercentText} من متوسطه"
                            : "",
                        // نص واحد مجمّع للبحث في المهارات والملاحظات بضربة واحدة
                        SkillsSearchText = string.Join(" ", skillNames) + " " + (w.SkillsNotes ?? "")
                    });
                }

                ApplyFilters();
                RefreshSummary();
            }
            finally
            {
                IsLoading = false;
            }

            // بعد ما الشاشة تجهز: لو وقت المراجعة الشهرية جه وفيه اقتراحات
            // فعلاً، البانر بيظهر. بيتنادى في الآخر عشان ميأخّرش عرض القايمة
            await CheckSkillReviewDueAsync();
        }

        // ------- لوحة التفاصيل -------

        private async Task LoadDetailAsync(WorkerRow? row)
        {
            if (row is null)
            {
                Detail = null;
                await FlushPendingRowRefreshAsync();
                return;
            }

            // ساب العامل وهو لسه في وضع الإضافة: جلسة الإضافة خلصت
            // بمغادرته، فبننفّذ التحديث المؤجّل دلوقتي عشان عدّاد مهاراته
            // في القائمة ميفضلش قديم. إعادة الاختيار اللي جوّه التحديث
            // بتنادي الدالة دي تاني بالصف الجديد، فبنسيبها هي تكمّل.
            if (_skillRowsStale && Detail is not null && Detail.WorkerId != row.WorkerId)
            {
                Detail.IsAddingSkills = false;
                await FlushPendingRowRefreshAsync();
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
            var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

            var worker = await workerRepo.GetWithSkillsAsync(row.WorkerId);
            if (worker is null) return;

            // إضافة/إزالة مهارة بتعيد تحميل البروفايل. من غير ده الكارت المفتوح
            // بيتقفل والبحث بيضيع مع كل مرحلة بيضيفها — والمستخدم عادة بيضيف
            // كذا مرحلة ورا بعض في نفس المنتج
            var previous = Detail?.WorkerId == row.WorkerId ? Detail : null;

            // الهستوري الأسبوعي: آخر 8 أسابيع كافية للعرض السريع في البروفايل
            var history = await weeklyService.GetWorkerWeeklyHistoryAsync(
                worker.Id, DateTime.Today.AddDays(-7 * 8), DateTime.Today);

            // استعلام واحد بكل المنتجات ومراحلها (بما فيها الموقوف)
            var ownedStageIds = worker.Skills.Select(s => s.ProductionStageId).ToHashSet();
            var products = await productRepo.GetAllWithStagesAsync();

            // النجوم والقياس لكل مهارة — مفهرسة بالمرحلة عشان البناء
            // ميعملش استعلام لكل صف
            var ratingByStage = worker.Skills.ToDictionary(
                s => s.ProductionStageId,
                s => (s.Stars, s.MeasuredRatio, s.MeasuredDays));

            var skillGroups = BuildSkillGroups(products, ownedStageIds, ratingByStage);

            Detail = new WorkerDetail
            {
                WorkerId = worker.Id,
                FullName = worker.FullName,
                PhoneNumber = worker.PhoneNumber ?? "—",
                HireDateText = worker.HireDate?.ToString("yyyy/MM/dd") ?? "—",
                PhotoData = worker.PhotoData,
                IsActive = worker.IsActive,
                HourlyRole = worker.HourlyRole,
                HourlyRoleText = worker.HourlyRole?.ToArabicName() ?? "",
                DailyWageEgp = worker.DailyWageEgp,
                WageText = worker.DailyWageEgp > 0
                    ? $"سعر اليومية: {worker.DailyWageEgp:N0} جنيه"
                    : "سعر اليومية: لم يُحدد",
                AllGroups = skillGroups,
                WeeklyHistory = new ObservableCollection<WeekHistoryItem>(history.Select(h => new WeekHistoryItem
                {
                    WeekTitle = $"{h.WeekStart:dd/MM} — {h.WeekEnd:dd/MM}",
                    RelativeLabel = DescribeWeek(h.WeekStart, h.WeekEnd),
                    Produced = h.ProducedWorkdays,
                    AbsenceDeduction = h.AbsenceDeduction,
                    PenaltyDeduction = h.PenaltyDeduction,
                    Net = h.NetWorkdays,
                    // أجر الأسبوع بالجنيه (بيظهر بس لو ليه سعر يومية)
                    WageText = h.DailyWageEgp > 0 ? $"{h.NetWageEgp:N0} ج" : "",
                    IsBest = h.IsBestWorkerOfWeek,
                    // تفاصيل كصفوف مش كسطر نص — عشان تتقرا وتتحاذى
                    Breakdown = new ObservableCollection<WeekStageRow>(
                        h.Breakdown
                         .OrderByDescending(b => b.PieceCount)
                         .Select(b => new WeekStageRow
                         {
                             ProductName = b.ProductName,
                             StageName = b.StageName,
                             PieceCount = b.PieceCount
                         })),
                    Penalties = new ObservableCollection<WeekPenaltyRow>(
                        h.Penalties.Select(p => new WeekPenaltyRow
                        {
                            Reason = p.Reason,
                            DeductionName = p.DeductionName,
                            DateText = $"{p.Date:dd/MM}"
                        }))
                }))
            };

            Detail.ApplyGroupMode();
            RestorePanelState(previous);
        }

        /// <summary>
        /// تسمية الأسبوع بالنسبة للنهارده. "من 07/30 إلى 08/05" لوحدها بتخلي
        /// المستخدم يحسب بنفسه هو ده أنهي أسبوع — الاسم بيوفّر عليه الحسبة.
        /// </summary>
        private static string DescribeWeek(DateTime weekStart, DateTime weekEnd)
        {
            var today = DateTime.Today;
            if (today >= weekStart.Date && today <= weekEnd.Date) return "الأسبوع الحالي";
            if (today >= weekStart.Date.AddDays(7) && today <= weekEnd.Date.AddDays(7)) return "الأسبوع اللي فات";
            return "";
        }

        /// <summary>يفتح كارت أسبوع ويقفل اللي كان مفتوح (نفس أكورديون المهارات)</summary>
        [RelayCommand]
        private void ToggleWeek(WeekHistoryItem? week)
        {
            if (week is null || Detail is null) return;
            if (week.IsEmptyWeek) return; // مفيش تفاصيل تتفتح أصلاً

            var opening = !week.IsExpanded;
            foreach (var other in Detail.WeeklyHistory) other.IsExpanded = false;
            week.IsExpanded = opening;
        }

        /// <summary>
        /// يرجّع حالة لوحة المهارات بعد إعادة التحميل: نص البحث، الكارت المفتوح،
        /// ولوحة الإضافة لو كانت مفتوحة. الترتيب مهم — البحث بيقفل كل الكروت
        /// أول ما يتطبّق، فاستعادة الكارت المفتوح لازم تيجي بعده.
        /// </summary>
        private void RestorePanelState(WorkerDetail? previous)
        {
            if (previous is null || Detail is null) return;

            Detail.IsAddingSkills = previous.IsAddingSkills;
            Detail.SkillSearch = previous.SkillSearch;

            if (previous.SkillSearch.Trim().Length > 0) return; // البحث بيفتح الكروت بنفسه

            var openId = previous.SkillProducts.FirstOrDefault(g => g.IsExpanded)?.ProductId;
            if (openId is null) return;

            var reopened = Detail.SkillProducts.FirstOrDefault(g => g.ProductId == openId);
            if (reopened is not null) reopened.IsExpanded = true;

            Detail.RefreshExpandState();
        }

        /// <summary>
        /// بيبني كارت لكل منتج في المصنع. جوّه الكارت بتتعرض **كل** مراحل الخط
        /// بترتيبها — اللي بيعرفها واللي لأ — عشان الفجوة تبان وتتسدّ في مكانها.
        ///
        /// الكروت كلها بتتبني هنا مرة واحدة، و<see cref="WorkerDetail.ApplyGroupMode"/>
        /// هي اللي بتقرر يتعرض منها إيه: مهاراته بس، ولا كل المنتجات (وضع الإضافة).
        /// كده فورم الإضافة هو نفس الكارت مش شاشة تانية يتعلّمها من الأول.
        ///
        /// الترتيب بالتغطية تنازليًا: المنتجات اللي العامل قريب من تغطيتها
        /// بالكامل هي اللي المستخدم بيهتم بيها الأول (فاضل مرحلتين وتخلص).
        /// </summary>
        private static List<SkillProductGroup> BuildSkillGroups(
            IReadOnlyList<Core.Models.Product> products,
            HashSet<int> ownedStageIds,
            IReadOnlyDictionary<int, (int Stars, decimal Ratio, int Days)> ratingByStage)
        {
            var groups = new List<SkillProductGroup>();

            foreach (var product in products)
            {
                var group = new SkillProductGroup
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    IsProductInactive = !product.IsActive
                };

                var ordered = product.Stages.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var stage = ordered[i];

                    // مرحلة موقوفة والعامل مش بيعرفها = ضوضاء خالصة، مبتتعرضش.
                    // لكن لو بيعرفها بتفضل ظاهرة بعلامة تحذير عشان يشيلها.
                    if (!stage.IsActive && !ownedStageIds.Contains(stage.Id)) continue;

                    ratingByStage.TryGetValue(stage.Id, out var rating);

                    group.Stages.Add(new SkillStageItem
                    {
                        StageId = stage.Id,
                        ProductId = product.Id,
                        StageName = stage.StageName,
                        Position = i + 1,
                        IsStageInactive = !stage.IsActive,
                        IsKnown = ownedStageIds.Contains(stage.Id),
                        // المرحلة اللي العامل مش بيعرفها مالهاش تقييم —
                        // القيمة المحايدة بتتعرض بس مش بتتحفظ
                        Stars = rating.Stars == 0 ? SkillRatingService.DefaultStars : rating.Stars,
                        MeasuredRatio = rating.Ratio,
                        MeasuredDays = rating.Days
                    });
                }

                groups.Add(group);
            }

            return groups
                .OrderByDescending(g => g.ActiveCount > 0 ? (double)g.KnownCount / g.ActiveCount : 0)
                .ThenByDescending(g => g.KnownCount)
                .ThenBy(g => g.ProductName)
                .ToList();
        }

        // ------- أوامر الإدارة -------

        [RelayCommand]
        private async Task AddWorkerAsync()
        {
            var dialog = new WorkerEditDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                var created = await mgmt.CreateWorkerAsync(
                    dialog.WorkerName, dialog.PhoneNumber,
                    dialog.HireDate, dialog.HourlyRole, dialog.DailyWageEgp);

                if (dialog.PhotoData is not null)
                    await mgmt.SetWorkerPhotoAsync(created.Id, dialog.PhotoData);

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة العامل");
            }
        }

        [RelayCommand]
        private async Task EditWorkerAsync()
        {
            if (SelectedWorker is null || Detail is null) return;

            var dialog = new WorkerEditDialog
            {
                Owner = Application.Current.MainWindow,
                Title = "تعديل بيانات عامل"
            };
            dialog.LoadWorker(Detail.FullName,
                Detail.PhoneNumber == "—" ? null : Detail.PhoneNumber,
                Detail.HireDateText == "—" ? null : DateTime.Parse(Detail.HireDateText),
                Detail.HourlyRole, Detail.DailyWageEgp, Detail.PhotoData);

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                await mgmt.UpdateWorkerAsync(
                    SelectedWorker.WorkerId, dialog.WorkerName,
                    dialog.PhoneNumber, dialog.HireDate, dialog.HourlyRole, dialog.DailyWageEgp);

                // الصورة بتتحفظ بس لو المستخدم غيّرها فعلاً
                if (dialog.PhotoChanged)
                    await mgmt.SetWorkerPhotoAsync(SelectedWorker.WorkerId, dialog.PhotoData);

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في تعديل العامل");
            }
        }

        [RelayCommand]
        private async Task ToggleActiveAsync()
        {
            if (SelectedWorker is null) return;

            // رسالة تأكيد مختلفة حسب الحالة الحالية — الإيقاف قرار أكبر من التفعيل
            var isDeactivating = SelectedWorker.IsActive;
            var message = isDeactivating
                ? $"إيقاف العامل \"{SelectedWorker.FullName}\"؟\nهيختفي من القوائم لكن كل سجلاته التاريخية هتفضل محفوظة."
                : $"إعادة تفعيل العامل \"{SelectedWorker.FullName}\"؟";

            if (!Notify.Ask(message, "تأكيد"))
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateWorkerAsync(SelectedWorker.WorkerId);
            else
                await mgmt.ReactivateWorkerAsync(SelectedWorker.WorkerId);

            await LoadAsync();
        }

        // ======================= المراجعة الشهرية للتقييمات =======================

        /// <summary>فيه تقييمات محتاجة مراجعة؟ (بيتحكم في ظهور البانر)</summary>
        [ObservableProperty]
        private bool _needsSkillReview;

        [ObservableProperty]
        private string _skillReviewText = "";

        /// <summary>
        /// بيشوف لو وقت المراجعة الشهرية جه وفيه فعلاً اقتراحات.
        ///
        /// الشرطين مع بعض مقصودين: التذكير من غير اقتراحات ضوضاء، والاقتراحات
        /// كل يوم بتخلي المستخدم يتعلّم يتجاهل البانر. فمرة كل شهر، ولو فيه
        /// حاجة تستاهل بس.
        /// </summary>
        private async Task CheckSkillReviewDueAsync()
        {
            var settings = AppSettingsStore.Load();
            var lastReview = settings.LastSkillReviewAt;

            var due = lastReview is null ||
                      (DateTime.Today - lastReview.Value.Date).TotalDays >= SkillRatingService.ReviewIntervalDays;

            if (!due)
            {
                NeedsSkillReview = false;
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var review = await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                .BuildReviewAsync(DateTime.Today);

            NeedsSkillReview = review.HasSuggestions;
            SkillReviewText = review.HasSuggestions
                ? $"وقت مراجعة تقييمات العمال — {review.SummaryText}"
                : "";
        }

        /// <summary>يفتح نافذة المراجعة ويسجّل إن المدير راجع</summary>
        [RelayCommand]
        private async Task OpenSkillReviewAsync()
        {
            SkillReviewDto review;
            using (var scope = _scopeFactory.CreateScope())
                review = await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                    .BuildReviewAsync(DateTime.Today);

            var applied = SkillReviewDialog.Show(Application.Current.MainWindow, _scopeFactory, review);

            // التاريخ بيتسجّل حتى لو المدير تجاهل الكل: هو راجع فعلاً،
            // ولو التذكير فضل ظاهر هيتحوّل لضوضاء بيتعلّم يتجاهلها
            var settings = AppSettingsStore.Load();
            settings.LastSkillReviewAt = DateTime.Today;
            AppSettingsStore.Save(settings);

            NeedsSkillReview = false;

            if (applied > 0)
            {
                await LoadAsync();
                Notify.Info($"اتحفظ {applied} تقييم. العمال هيترتبوا بالتقييمات دي في شاشة التسجيل.", "تم");
            }
        }

        /// <summary>
        /// يشيل العامل من النظام نهائيًا — غير الإيقاف.
        ///
        /// الإيقاف مؤقت وله رجوع (إجازة/وقف شغل)، والحذف بيقول "مبقاش من
        /// المصنع". الاتنين بيخفوه من القوايم، بس الحذف بيمر على بوابة
        /// كلمة السر وبيتسجّل في سجل العمليات بمين شاله وليه.
        ///
        /// سجلاته التاريخية (إنتاج، أجور، حضور) بتفضل كلها زي ما هي.
        /// </summary>
        [RelayCommand]
        private async Task DeleteWorkerAsync()
        {
            if (SelectedWorker is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

                var input = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "حذف عامل",
                    $"{SelectedWorker.FullName} — هيختفي من كل القوايم. سجلات إنتاجه وأجوره القديمة هتفضل محفوظة ومقروءة.",
                    SensitiveActionKind.Delete,
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                var result = await mgmt.DeleteWorkerAsync(
                    SelectedWorker.WorkerId, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    Notify.Warn(result.Message, "مش هينفع");
                    return;
                }

                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الحذف");
            }
        }

        /// <summary>
        /// يقلب وضع الإضافة: الكروت بتتوسّع لتشمل المنتجات اللي العامل مالوش
        /// فيها ولا مهارة، فيقدر يفتح أي منتج ويضيف منه.
        ///
        /// وهو شغال مفيش أي إعادة تحميل للقائمة — اللوحة بتفضل زي ما هي
        /// بالظبط لحد ما يدوس "خلصت"، ووقتها بس بتتحدّث.
        /// </summary>
        [RelayCommand]
        private async Task ToggleAddSkillsAsync()
        {
            if (Detail is null) return;

            Detail.IsAddingSkills = !Detail.IsAddingSkills;
            Detail.SkillSearch = string.Empty; // البحث بتاع وضع بيلخبط في الوضع التاني

            if (!Detail.IsAddingSkills) await FlushPendingRowRefreshAsync();
        }

        /// <summary>
        /// يضيف كل مراحل الخط اللي العامل مش بيعرفها مرة واحدة. عامل بيغطي
        /// خط من 14 مرحلة كان محتاج 14 دوسة + 14 إعادة تحميل.
        /// </summary>
        [RelayCommand]
        private async Task AddAllStagesAsync(SkillProductGroup? group)
        {
            if (group is null || Detail is null) return;

            // المراحل الموقوفة مستثناة — إضافة مهارة عليها مالهاش أي معنى
            var missing = group.Stages.Where(s => !s.IsKnown && !s.IsStageInactive).ToList();
            if (missing.Count == 0) return;

            var message = $"إضافة كل مراحل \"{group.ProductName}\" الناقصة للعامل؟\n" +
                          $"عدد المراحل: {missing.Count}";
            if (!Notify.Ask(message, "تأكيد"))
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            foreach (var stage in missing)
            {
                await mgmt.AssignSkillAsync(Detail.WorkerId, stage.StageId);

                stage.IsKnown = true;
                stage.Stars = SkillRatingService.DefaultStars;
            }

            // زي ToggleSkillAsync: تحديث في المكان عشان اللوحة متتبنيش من
            // الأول والكارت المفتوح ميتقفلش
            group.RefreshCounters();
            Detail.RefreshCoverage(missing[0].StageId);
            await RefreshRowsKeepingSelectionAsync();
        }

        /// <summary>
        /// يقلب حالة المرحلة: بيعرفها ← مش بيعرفها. زرار واحد للاتنين لأن
        /// المرحلة معروضة في مكانها في الخط سواء بيعرفها أو لأ — فسدّ الفجوة
        /// وشيل المهارة الغلط بقوا نفس الحركة.
        ///
        /// **مفيش إعادة تحميل للبروفايل هنا عن قصد.** كانت بتتنادى بعد كل
        /// إضافة، فاللوحة كانت بتتبني من الأول والكارت المفتوح بيتقفل —
        /// والمستخدم بيضيف عشر مهارات ورا بعض، يعني عشر مرات بيدوّر على
        /// مكانه تاني. التحديث بقى في مكانه، واللوحة بتفضل زي ما هي.
        /// </summary>
        [RelayCommand]
        private async Task ToggleSkillAsync(SkillStageItem? stage)
        {
            if (stage is null || Detail is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            var adding = !stage.IsKnown;

            if (adding) await mgmt.AssignSkillAsync(Detail.WorkerId, stage.StageId);
            else await mgmt.RemoveSkillAsync(Detail.WorkerId, stage.StageId);

            stage.IsKnown = adding;
            if (adding) stage.Stars = SkillRatingService.DefaultStars;

            Detail.RefreshCoverage(stage.StageId);
            await RefreshRowsKeepingSelectionAsync();
        }

        /// <summary>
        /// يحطّ تقييم المدير بالنجوم على مهارة.
        ///
        /// ولو المرحلة لسه مش مضافة (وضع الإضافة)، بيضيفها **بالتقييم ده**
        /// في حركة واحدة. ده اللي بيخلي "مستوى العامل في المرحلة" جزء من
        /// إضافتها مش خطوة تانية بعدها — والقيمة بتروح لنظام النجوم نفسه
        /// (SkillRatingService)، مفيش حقل تقييم تاني موازي.
        ///
        /// المعامل بييجي كنص "stageId:stars" من زرار النجمة — WPF
        /// مبيبعتش معاملين، والبديل (خمس أوامر لكل نجمة) كان هيكرر نفس
        /// الكود خمس مرات.
        /// </summary>
        [RelayCommand]
        private async Task SetSkillStarsAsync(string? parameter)
        {
            if (Detail is null || string.IsNullOrWhiteSpace(parameter)) return;

            var parts = parameter.Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var stageId) ||
                !int.TryParse(parts[1], out var stars)) return;

            var item = Detail.SkillProducts
                .SelectMany(g => g.Stages)
                .FirstOrDefault(s => s.StageId == stageId);

            // مرحلة مش معروضة، أو مش مضافة ومش في وضع الإضافة = مفيش حاجة تتعمل
            if (item is null || !item.ShowStars) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();

                // مرحلة جديدة: الربط الأول، وبعدين التقييم. الترتيب مهم —
                // SetStarsAsync بيرمي "العامل ده مش مربوط بالمرحلة دي"
                if (!item.IsKnown)
                    await scope.ServiceProvider.GetRequiredService<WorkerManagementService>()
                        .AssignSkillAsync(Detail.WorkerId, stageId);

                await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                    .SetStarsAsync(Detail.WorkerId, stageId, stars);

                // تحديث الصف في مكانه بدل إعادة تحميل البروفايل كله —
                // إعادة التحميل بتقفل الكارت المفتوح والمستخدم بيضيع
                var wasNew = !item.IsKnown;
                item.Stars = stars;
                item.IsKnown = true;

                // أي تغيير في النجوم بيحرّك متوسط المنتج، والشارة اللي على
                // رأس الكارت محسوبة منه — فلازم تتحدّث حتى لو المهارة مش جديدة
                Detail.RefreshCoverage(stageId);

                // الإضافة كمان بتغيّر عدّاد المهارات في كارت القائمة
                if (wasNew) await RefreshRowsKeepingSelectionAsync();
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
        }

        /// <summary>
        /// يفتح كارت منتج ويقفل اللي كان مفتوح. أكورديون بكارت واحد مفتوح
        /// عشان اللوحة تفضل قصيرة حتى مع عامل عنده ١٠ منتجات.
        /// </summary>
        [RelayCommand]
        private void ToggleSkillGroup(SkillProductGroup? group)
        {
            if (group is null || Detail is null) return;

            var opening = !group.IsExpanded;
            foreach (var other in Detail.SkillProducts) other.IsExpanded = false;
            group.IsExpanded = opening;

            Detail.RefreshExpandState();
        }

        /// <summary>يفتح كل الكروت أو يقفلها — للمراجعة السريعة لكل الخطوط</summary>
        [RelayCommand]
        private void ToggleAllSkillGroups()
        {
            if (Detail is null) return;

            var expand = !Detail.AllExpanded;
            foreach (var group in Detail.SkillProducts)
                if (group.IsVisible) group.IsExpanded = expand;

            Detail.RefreshExpandState();
        }

        /// <summary>
        /// يعيد تحميل القائمة من غير ما يضيّع العامل المحدد ولا يقفل
        /// لوحة التفاصيل (LoadAsync لوحدها بتصفّر الاختيار).
        ///
        /// في وضع الإضافة بيتأجّل بالكامل — شوف <see cref="_skillRowsStale"/>.
        /// </summary>
        private async Task RefreshRowsKeepingSelectionAsync()
        {
            if (Detail?.IsAddingSkills == true)
            {
                _skillRowsStale = true;
                return;
            }

            var selectedId = SelectedWorker?.WorkerId;

            _reloadingRows = true;
            try { await LoadAsync(); }
            finally { _reloadingRows = false; }

            if (selectedId is null) return;
            SelectedWorker = Workers.FirstOrDefault(w => w.WorkerId == selectedId.Value);
        }

        /// <summary>
        /// ينفّذ التحديث المؤجّل لو فيه. بيصفّر العلم **قبل** ما يحدّث عشان
        /// إعادة الاختيار اللي جوّه التحديث متنادّيش نفسها تاني.
        /// </summary>
        private async Task FlushPendingRowRefreshAsync()
        {
            if (!_skillRowsStale) return;
            _skillRowsStale = false;
            await RefreshRowsKeepingSelectionAsync();
        }
    }
}
