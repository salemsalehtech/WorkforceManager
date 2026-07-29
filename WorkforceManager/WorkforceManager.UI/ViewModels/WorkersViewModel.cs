using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
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

        /// <summary>نص البحث الموحّد: اسم عامل أو اسم مرحلة/منتج</summary>
        [ObservableProperty]
        private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) => ApplyFilters();

        /// <summary>الفلتر السريع المختار (الكل / بالقطعة / بالساعة / موقوفين)</summary>
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

        public string BestWorkerName =>
            _allWorkers.FirstOrDefault(w => w.IsBestOfWeek)?.FullName ?? "—";

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

            IEnumerable<WorkerRow> result = ActiveFilter switch
            {
                WorkerFilter.PieceRate => _allWorkers.Where(w => w.IsActive && !w.IsHourly),
                WorkerFilter.Hourly => _allWorkers.Where(w => w.IsActive && w.IsHourly),
                WorkerFilter.Inactive => _allWorkers.Where(w => !w.IsActive),
                _ => _allWorkers.Where(w => w.IsActive)
            };

            // البحث بيشمل الاسم والمهارات (اسم المرحلة أو المنتج) والملاحظات —
            // نفس نطاق البحث القديم بالظبط بس بقى لحظي
            if (query.Length > 0)
                result = result.Where(w =>
                    w.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    w.SkillsSearchText.Contains(query, StringComparison.OrdinalIgnoreCase));

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
        }

        /// <summary>يمسح البحث ويرجّع الفلتر للكل</summary>
        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
            ActiveFilter = WorkerFilter.All;
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
            // تحميل بروفايل العامل المحدد (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(() => LoadDetailAsync(value));
        }

        // ------- تحميل القائمة -------

        [RelayCommand]
        public async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
                var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

                var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);
                WeekTitle = $"الأسبوع الحالي: من الخميس {weekStart:yyyy/MM/dd} إلى الأربعاء {weekEnd:yyyy/MM/dd}";

                // إحصائيات الأسبوع الحالي لكل العمال (استعلام واحد مجمّع)
                var weekly = await weeklyService.GetTeamWeeklySummaryAsync(DateTime.Today);
                var weeklyByWorker = weekly.ToDictionary(w => w.WorkerId);

                // كل العمال بمهاراتهم مرة واحدة — الفلترة والبحث بعد كده في الذاكرة
                var workers = await workerRepo.GetAllWithSkillsAsync();

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
        }

        // ------- لوحة التفاصيل -------

        private async Task LoadDetailAsync(WorkerRow? row)
        {
            if (row is null)
            {
                Detail = null;
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
            var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

            var worker = await workerRepo.GetWithSkillsAsync(row.WorkerId);
            if (worker is null) return;

            // الهستوري الأسبوعي: آخر 8 أسابيع كافية للعرض السريع في البروفايل
            var history = await weeklyService.GetWorkerWeeklyHistoryAsync(
                worker.Id, DateTime.Today.AddDays(-7 * 8), DateTime.Today);

            // كل المراحل المتاحة (منتج — مرحلة) لإضافة مهارة جديدة،
            // والمهارات اللي عنده بالفعل متعلّمة عشان تتشال من قائمة الإضافة
            var ownedStageIds = worker.Skills.Select(s => s.ProductionStageId).ToHashSet();
            var products = await productRepo.GetActiveWithStagesAsync();
            var stageOptions = products
                .SelectMany(p => p.Stages
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                    .Select(s => new StageOption
                    {
                        StageId = s.Id,
                        ProductName = p.Name,
                        StageName = s.StageName,
                        Display = $"{p.Name} — {s.StageName}",
                        AlreadyOwned = ownedStageIds.Contains(s.Id)
                    }))
                .ToList();

            Detail = new WorkerDetail
            {
                WorkerId = worker.Id,
                FullName = worker.FullName,
                PhoneNumber = worker.PhoneNumber ?? "—",
                HireDateText = worker.HireDate?.ToString("yyyy/MM/dd") ?? "—",
                SkillsNotes = worker.SkillsNotes ?? "",
                IsActive = worker.IsActive,
                HourlyRole = worker.HourlyRole,
                HourlyRoleText = worker.HourlyRole?.ToArabicName() ?? "",
                DailyWageEgp = worker.DailyWageEgp,
                WageText = worker.DailyWageEgp > 0
                    ? $"سعر اليومية: {worker.DailyWageEgp:N0} جنيه"
                    : "سعر اليومية: لم يُحدد",
                Skills = new ObservableCollection<SkillItem>(worker.Skills.Select(s => new SkillItem
                {
                    StageId = s.ProductionStageId,
                    Display = $"{s.ProductionStage.Product.Name} — {s.ProductionStage.StageName}"
                })),
                WeeklyHistory = new ObservableCollection<WeekHistoryItem>(history.Select(h => new WeekHistoryItem
                {
                    WeekTitle = $"من {h.WeekStart:MM/dd} إلى {h.WeekEnd:MM/dd}",
                    Produced = h.ProducedWorkdays,
                    AbsenceDeduction = h.AbsenceDeduction,
                    PenaltyDeduction = h.PenaltyDeduction,
                    Net = h.NetWorkdays,
                    // أجر الأسبوع بالجنيه (بيظهر بس لو ليه سعر يومية)
                    WageText = h.DailyWageEgp > 0 ? $"{h.NetWageEgp:N0} ج" : "",
                    BestMark = h.IsBestWorkerOfWeek ? "⭐" : "",
                    // تفصيل المراحل اللي اشتغل عليها الأسبوع ده (بيظهر تحت السطر)
                    BreakdownText = string.Join("، ", h.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount} قطعة")),
                    // جزاءات الأسبوع بأسبابها
                    PenaltiesText = string.Join("، ", h.Penalties.Select(p => $"{p.Reason} ({p.DeductionName})"))
                })),
                AllStageOptions = stageOptions
            };

            Detail.ApplyStageFilter();
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
                await mgmt.CreateWorkerAsync(
                    dialog.WorkerName, dialog.PhoneNumber,
                    dialog.HireDate, dialog.SkillsNotes, dialog.HourlyRole, dialog.DailyWageEgp);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في إضافة العامل", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Detail.SkillsNotes, Detail.HourlyRole, Detail.DailyWageEgp);

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                await mgmt.UpdateWorkerAsync(
                    SelectedWorker.WorkerId, dialog.WorkerName,
                    dialog.PhoneNumber, dialog.HireDate, dialog.SkillsNotes, dialog.HourlyRole, dialog.DailyWageEgp);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في تعديل العامل", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (MessageBox.Show(message, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateWorkerAsync(SelectedWorker.WorkerId);
            else
                await mgmt.ReactivateWorkerAsync(SelectedWorker.WorkerId);

            await LoadAsync();
        }

        /// <summary>يفتح/يقفل لوحة إضافة المهارات</summary>
        [RelayCommand]
        private void ToggleAddSkills()
        {
            if (Detail is null) return;
            Detail.IsAddingSkills = !Detail.IsAddingSkills;
            if (!Detail.IsAddingSkills) Detail.StageSearch = string.Empty;
        }

        /// <summary>
        /// يضيف كل المراحل المعلّمة مرة واحدة. الإضافة الجماعية دي هي
        /// الفرق الكبير: عامل بـ 20 مرحلة كان محتاج 20 دورة كاملة
        /// (فتح قايمة → اختيار → إضافة → إعادة تحميل).
        /// </summary>
        [RelayCommand]
        private async Task AddSelectedSkillsAsync()
        {
            if (Detail is null) return;

            var chosen = Detail.AllStageOptions.Where(o => o.IsSelected && !o.AlreadyOwned).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show("علّم مرحلة واحدة على الأقل الأول", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            foreach (var option in chosen)
                await mgmt.AssignSkillAsync(Detail.WorkerId, option.StageId);

            // إعادة تحميل التفاصيل والقائمة (عدد المهارات على البطاقة بيتحدث)
            await LoadDetailAsync(SelectedWorker);
            await RefreshRowsKeepingSelectionAsync();
        }

        [RelayCommand]
        private async Task RemoveSkillAsync(SkillItem? skill)
        {
            if (skill is null || Detail is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
            await mgmt.RemoveSkillAsync(Detail.WorkerId, skill.StageId);

            await LoadDetailAsync(SelectedWorker);
            await RefreshRowsKeepingSelectionAsync();
        }

        /// <summary>
        /// يعيد تحميل القائمة من غير ما يضيّع العامل المحدد ولا يقفل
        /// لوحة التفاصيل (LoadAsync لوحدها بتصفّر الاختيار).
        /// </summary>
        private async Task RefreshRowsKeepingSelectionAsync()
        {
            var selectedId = SelectedWorker?.WorkerId;
            await LoadAsync();

            if (selectedId is null) return;
            SelectedWorker = Workers.FirstOrDefault(w => w.WorkerId == selectedId.Value);
        }
    }

    // ------- نماذج العرض (خاصة بالشاشة دي بس) -------

    /// <summary>الفلتر السريع فوق قائمة العمال</summary>
    public enum WorkerFilter { All, PieceRate, Hourly, Inactive }

    /// <summary>طرق ترتيب قائمة العمال</summary>
    public enum WorkerSort { Name, NetDesc, NetAsc, AbsenceDesc, SkillsDesc }

    /// <summary>خيار ترتيب في القائمة المنسدلة</summary>
    public record WorkerSortOption(WorkerSort Value, string Display);

    /// <summary>بطاقة عامل واحد في القائمة: بياناته + أرقام الأسبوع الحالي + تنبيهاته</summary>
    public class WorkerRow
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public bool IsActive { get; init; }
        public int PresentDays { get; init; }
        public int AbsentWithPermissionDays { get; init; }
        public int AbsentWithoutPermissionDays { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal NetWorkdays { get; init; }
        public bool IsBestOfWeek { get; init; }

        /// <summary>عامل بالساعة؟ (رص/جودة/تدريب)</summary>
        public bool IsHourly { get; init; }
        public string HourlyRoleText { get; init; } = "";
        public decimal DailyWageEgp { get; init; }
        public int SkillsCount { get; init; }

        /// <summary>كل مهاراته وملاحظاته في نص واحد — للبحث اللحظي من غير حسابات</summary>
        public string SkillsSearchText { get; init; } = "";

        // ------- العرض -------

        public string StatusText => IsActive ? "نشط" : "موقوف";
        public string TypeText => IsHourly ? HourlyRoleText : "بالقطعة";

        /// <summary>أول حرفين من الاسم للدايرة (نفس أسلوب شاشة الحضور)</summary>
        public string Initials
        {
            get
            {
                var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "؟";
                return parts.Length == 1
                    ? parts[0][..Math.Min(2, parts[0].Length)]
                    : $"{parts[0][0]}{parts[1][0]}";
            }
        }

        public string WageText => DailyWageEgp > 0 ? $"{DailyWageEgp:N0} ج/يوم" : "مفيش سعر";
        public string SkillsText => IsHourly ? "بالساعة" : $"{SkillsCount} مهارة";

        /// <summary>لون الصافي: أخضر لو موجب، أحمر لو سالب، رمادي لو صفر</summary>
        public string NetColor => NetWorkdays switch
        {
            > 0 => "#0B6E4F",
            < 0 => "#B00020",
            _ => "#6B7686"
        };

        // ------- التنبيهات -------

        /// <summary>مفيش سعر يومية — العامل ده هياخد صفر جنيه في كشف الأجور</summary>
        public bool HasNoWage => DailyWageEgp <= 0;

        /// <summary>
        /// عامل بالقطعة من غير أي مهارة مربوطة — مش هيظهر في أي رحلة إنتاج
        /// خالص (رحلة الإنتاج بتعرض المؤهلين بس)، فعمليًا مينفعش يشتغل.
        /// العامل بالساعة مستثنى لأنه أصلاً مالوش مهارات بالتصميم.
        /// </summary>
        public bool HasNoSkills => !IsHourly && SkillsCount == 0;

        public bool NeedsAttention => HasNoWage || HasNoSkills;

        /// <summary>نص التنبيه المعروض على البطاقة</summary>
        public string AttentionText => (HasNoWage, HasNoSkills) switch
        {
            (true, true) => "مفيش سعر يومية ولا مهارات",
            (true, false) => "مفيش سعر يومية — هياخد صفر في كشف الأجور",
            (false, true) => "مفيش مهارات — مش هيظهر في رحلات الإنتاج",
            _ => ""
        };
    }

    /// <summary>تفاصيل العامل المعروضة في اللوحة الجانبية (البروفايل)</summary>
    public partial class WorkerDetail : ObservableObject
    {
        public int WorkerId { get; init; }
        public string FullName { get; init; } = "";
        public string PhoneNumber { get; init; } = "";
        public string HireDateText { get; init; } = "";
        public string SkillsNotes { get; init; } = "";
        public bool IsActive { get; init; }

        /// <summary>دور العامل بالساعة (null = عامل إنتاج بالقطعة)</summary>
        public Core.Enums.HourlyRole? HourlyRole { get; init; }

        /// <summary>نص الدور بالساعة للعرض في البروفايل (فاضي لعامل الإنتاج)</summary>
        public string HourlyRoleText { get; init; } = "";

        /// <summary>سعر يومية العامل بالجنيه</summary>
        public decimal DailyWageEgp { get; init; }

        /// <summary>نص سعر اليومية للعرض في البروفايل</summary>
        public string WageText { get; init; } = "";

        /// <summary>مفيش سعر يومية — بيلوّن الشارة تحذير بدل أخضر</summary>
        public bool HasNoWage => DailyWageEgp <= 0;

        /// <summary>هل هو عامل بالساعة؟ (لإظهار شارة في البروفايل)</summary>
        public bool IsHourly => HourlyRole is not null;

        public ObservableCollection<SkillItem> Skills { get; init; } = new();
        public ObservableCollection<WeekHistoryItem> WeeklyHistory { get; init; } = new();

        /// <summary>كل المراحل اللي العامل لسه مش متأهل ليها (مصدر قائمة الإضافة)</summary>
        public List<StageOption> AllStageOptions { get; init; } = new();

        /// <summary>المراحل المعروضة دلوقتي في قائمة الإضافة (بعد فلترة البحث)</summary>
        public ObservableCollection<StageOption> VisibleStageOptions { get; } = new();

        /// <summary>بحث جوّه المراحل — القائمة ممكن تكون بمئات المراحل</summary>
        [ObservableProperty]
        private string _stageSearch = string.Empty;

        partial void OnStageSearchChanged(string value) => ApplyStageFilter();

        /// <summary>هل قائمة إضافة المهارات مفتوحة؟ (بتفضل مقفولة عشان متزحمش البروفايل)</summary>
        [ObservableProperty]
        private bool _isAddingSkills;

        public void ApplyStageFilter()
        {
            var query = StageSearch?.Trim() ?? "";

            var matches = AllStageOptions.Where(o => !o.AlreadyOwned);
            if (query.Length > 0)
                matches = matches.Where(o => o.Display.Contains(query, StringComparison.OrdinalIgnoreCase));

            VisibleStageOptions.Clear();
            foreach (var option in matches) VisibleStageOptions.Add(option);
        }

        /// <summary>عدد المراحل المعلّمة دلوقتي (بيظهر على زرار الإضافة)</summary>
        public int SelectedStageCount => AllStageOptions.Count(o => o.IsSelected);

        public bool HasSkills => Skills.Count > 0;
    }

    /// <summary>مهارة واحدة معروضة في البروفايل (منتج — مرحلة)</summary>
    public class SkillItem
    {
        public int StageId { get; init; }
        public string Display { get; init; } = "";
    }

    /// <summary>
    /// مرحلة في قائمة إضافة المهارات. بقت قابلة للتعليم (IsSelected) عشان
    /// المستخدم يعلّم كذا مرحلة ويضيفهم مرة واحدة، بدل ما يفتح القايمة
    /// ويضيف واحدة ويستنى التحميل ويكرر — العامل الواحد ممكن يكون له
    /// ٢٠ مرحلة.
    /// </summary>
    public partial class StageOption : ObservableObject
    {
        public int StageId { get; init; }
        public string Display { get; init; } = "";

        /// <summary>اسم المنتج لوحده — للتجميع في القائمة</summary>
        public string ProductName { get; init; } = "";

        /// <summary>اسم المرحلة لوحده — للعرض تحت اسم المنتج</summary>
        public string StageName { get; init; } = "";

        /// <summary>العامل عنده المهارة دي بالفعل؟ (بتتخفي من قائمة الإضافة)</summary>
        public bool AlreadyOwned { get; set; }

        [ObservableProperty]
        private bool _isSelected;
    }

    /// <summary>ملخص أسبوع واحد في هستوري العامل</summary>
    public class WeekHistoryItem
    {
        public string WeekTitle { get; init; } = "";
        public decimal Produced { get; init; }
        public decimal AbsenceDeduction { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal Net { get; init; }
        public string WageText { get; init; } = "";
        public string BestMark { get; init; } = "";
        public string BreakdownText { get; init; } = "";
        public string PenaltiesText { get; init; } = "";

        /// <summary>هل فيه تفاصيل إنتاج/جزاءات تستحق العرض؟ (لإخفاء السطور الفاضية)</summary>
        public bool HasBreakdown => !string.IsNullOrEmpty(BreakdownText);
        public bool HasPenalties => !string.IsNullOrEmpty(PenaltiesText);
        public bool HasWage => !string.IsNullOrEmpty(WageText);
    }
}
