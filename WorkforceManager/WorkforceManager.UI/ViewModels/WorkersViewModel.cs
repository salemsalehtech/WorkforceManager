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
                SkillsNotes = worker.SkillsNotes ?? "",
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
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();
                var result = await mgmt.DeleteWorkerAsync(
                    SelectedWorker.WorkerId, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    MessageBox.Show(result.Message, "مش هينفع", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// يقلب وضع الإضافة: الكروت بتتوسّع لتشمل المنتجات اللي العامل مالوش
        /// فيها ولا مهارة، فيقدر يفتح أي منتج ويضيف منه.
        /// </summary>
        [RelayCommand]
        private void ToggleAddSkills()
        {
            if (Detail is null) return;

            Detail.IsAddingSkills = !Detail.IsAddingSkills;
            Detail.SkillSearch = string.Empty; // البحث بتاع وضع بيلخبط في الوضع التاني
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
            if (MessageBox.Show(message, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            foreach (var stage in missing)
                await mgmt.AssignSkillAsync(Detail.WorkerId, stage.StageId);

            await LoadDetailAsync(SelectedWorker);
            await RefreshRowsKeepingSelectionAsync();
        }

        /// <summary>
        /// يقلب حالة المرحلة: بيعرفها ← مش بيعرفها. زرار واحد للاتنين لأن
        /// المرحلة معروضة في مكانها في الخط سواء بيعرفها أو لأ — فسدّ الفجوة
        /// وشيل المهارة الغلط بقوا نفس الحركة.
        /// </summary>
        [RelayCommand]
        private async Task ToggleSkillAsync(SkillStageItem? stage)
        {
            if (stage is null || Detail is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<WorkerManagementService>();

            if (stage.IsKnown) await mgmt.RemoveSkillAsync(Detail.WorkerId, stage.StageId);
            else await mgmt.AssignSkillAsync(Detail.WorkerId, stage.StageId);

            await LoadDetailAsync(SelectedWorker);
            await RefreshRowsKeepingSelectionAsync();
        }

        /// <summary>
        /// يحطّ تقييم المدير بالنجوم على مهارة.
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

            // التقييم للمهارات اللي العامل بيعرفها بس — مفيش معنى لتقييم
            // مرحلة هو أصلاً مش مربوط بيها
            if (item is null || !item.IsKnown) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SkillRatingService>()
                    .SetStarsAsync(Detail.WorkerId, stageId, stars);

                // تحديث الصف في مكانه بدل إعادة تحميل البروفايل كله —
                // إعادة التحميل بتقفل الكارت المفتوح والمستخدم بيضيع
                item.Stars = stars;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "مش هينفع", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // سعر اليومية مقصود إنه مش معروض على الكارت — بيان حساس، بيتشاف من
        // البروفايل بس. DailyWageEgp باقي هنا للتنبيه (HasNoWage) والترتيب فقط.
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

        /// <summary>مهارات العامل مجمّعة في كارت لكل منتج (مرتبة بالتغطية)</summary>
        public ObservableCollection<SkillProductGroup> SkillProducts { get; init; } = new();

        public ObservableCollection<WeekHistoryItem> WeeklyHistory { get; init; } = new();

        /// <summary>
        /// كل كروت المنتجات (بما فيها اللي العامل مالوش فيها ولا مهارة).
        /// SkillProducts فوق هي العرض المفلتر منها حسب الوضع الحالي.
        /// </summary>
        public List<SkillProductGroup> AllGroups { get; init; } = new();

        /// <summary>
        /// وضع الإضافة. مفيش فورم منفصل: نفس الكروت بالظبط، بس بتتوسّع لتشمل
        /// المنتجات اللي العامل مالوش فيها مهارة (بتبان 0 / 11) — يفتح المنتج
        /// ويضيف مراحله من نفس الكارت اللي اتعوّد عليه.
        /// </summary>
        [ObservableProperty]
        private bool _isAddingSkills;

        partial void OnIsAddingSkillsChanged(bool value)
        {
            ApplyGroupMode();
            OnPropertyChanged(nameof(SkillSearchHint));
        }

        public void ApplyGroupMode()
        {
            SkillProducts.Clear();

            foreach (var group in AllGroups)
            {
                var known = group.KnownCount > 0 || group.InactiveSkillCount > 0;

                // وضع الإضافة بيضيف المنتجات النشطة اللي لسه مالوش فيها حاجة.
                // منتج موقوف ومالوش فيه مهارة مبيظهرش أبدًا — مينفعش يشتغل عليه
                var show = IsAddingSkills ? known || !group.IsProductInactive : known;
                if (show) SkillProducts.Add(group);
            }

            ApplySkillFilter();
            OnPropertyChanged(nameof(HasSkills));
            RefreshExpandState();
        }

        /// <summary>فيه كروت معروضة دلوقتي؟ (شريط البحث بيظهر بيها)</summary>
        public bool HasSkills => SkillProducts.Count > 0;

        /// <summary>العامل مالوش ولا مهارة خالص — تحذير مختلف عن "البحث مالوش نتيجة"</summary>
        public bool HasAnySkill => AllGroups.Any(g => g.KnownCount > 0 || g.InactiveSkillCount > 0);

        /// <summary>نص خانة البحث بيتغير حسب الوضع</summary>
        public string SkillSearchHint => IsAddingSkills ? "دوّر على منتج أو مرحلة…" : "دوّر في مهاراته…";

        // ------- البحث جوّه مهارات العامل نفسه -------

        /// <summary>
        /// بحث في كروت المهارات. مع ٦٩ مهارة موزعة على منتجات كتير، الوصول
        /// لمرحلة معينة بالتمرير بطيء. البحث بيطابق اسم المنتج أو اسم المرحلة،
        /// وبيفتح الكروت المطابقة تلقائيًا عشان النتيجة تبان من غير دوسة زيادة.
        /// </summary>
        [ObservableProperty]
        private string _skillSearch = string.Empty;

        partial void OnSkillSearchChanged(string value) => ApplySkillFilter();

        public void ApplySkillFilter()
        {
            var query = SkillSearch?.Trim() ?? "";

            foreach (var group in SkillProducts)
            {
                if (query.Length == 0)
                {
                    foreach (var stage in group.Stages) stage.IsVisible = true;
                    group.IsVisible = true;
                    group.IsExpanded = false; // البحث الفاضي بيرجّع اللوحة لحالتها المرتبة
                    continue;
                }

                var productMatches = ArabicSearch.Contains(group.ProductName, query);

                // منتج مطابق بالاسم = كل مراحله تبان، مش المطابقة منها بس
                foreach (var stage in group.Stages)
                    stage.IsVisible = productMatches || ArabicSearch.Contains(stage.StageName, query);

                group.IsVisible = productMatches || group.Stages.Any(s => s.IsVisible);
                group.IsExpanded = group.IsVisible;
            }

            OnPropertyChanged(nameof(HasVisibleSkills));
        }

        /// <summary>البحث مالوش نتيجة — الفرق بين "مفيش مهارات" و"مفيش نتيجة"</summary>
        public bool HasVisibleSkills => SkillProducts.Any(g => g.IsVisible);

        /// <summary>كل الكروت مفتوحة دلوقتي؟ (بيقلب نص وأيقونة زرار فتح/قفل الكل)</summary>
        public bool AllExpanded => SkillProducts.Count > 0 && SkillProducts.All(g => g.IsExpanded);

        public void RefreshExpandState() => OnPropertyChanged(nameof(AllExpanded));
    }

    /// <summary>
    /// كارت منتج واحد في لوحة مهارات العامل. العامل ممكن يكون عنده ٦٩ مهارة،
    /// وعرضها كقائمة مسطّحة بيخلي البروفايل جدار نص مقروش. الكارت بيتقفل على
    /// اسم المنتج وتغطيته، وبيتفتح على مراحل الخط بترتيبها.
    /// </summary>
    public partial class SkillProductGroup : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";

        /// <summary>المنتج نفسه موقوف؟ (مهاراته باقية بس مش هتشتغل)</summary>
        public bool IsProductInactive { get; init; }

        /// <summary>كل مراحل الخط — اللي بيعرفها واللي لأ، بترتيب الخط</summary>
        public ObservableCollection<SkillStageItem> Stages { get; init; } = new();

        /// <summary>
        /// التغطية بتتحسب على المراحل النشطة بس. مرحلة موقوفة مش بتزوّد
        /// التغطية ولا بتنقّصها — هي خارج الحساب أصلاً عشان متديش إحساس
        /// كاذب إن العامل مغطي الخط.
        /// </summary>
        public int KnownCount => Stages.Count(s => s.IsKnown && !s.IsStageInactive);
        public int ActiveCount => Stages.Count(s => !s.IsStageInactive);

        public string CoverageText => $"{KnownCount} / {ActiveCount} مرحلة";

        /// <summary>بيعرف كل مراحل الخط النشطة — يقدر يمسك المنتج لوحده</summary>
        public bool CoversWholeLine => ActiveCount > 0 && KnownCount == ActiveCount;

        /// <summary>عنده مهارة على مرحلة موقوفة — بتتعلّم بعلامة تحذير</summary>
        public int InactiveSkillCount => Stages.Count(s => s.IsKnown && s.IsStageInactive);
        public bool HasInactiveSkills => InactiveSkillCount > 0;
        public string InactiveSkillsText => $"{InactiveSkillCount} مرحلة موقوفة";

        /// <summary>مراحل نشطة مش بيعرفها — بيظهر زرار "ضيفهم كلهم" لما تبقى موجودة</summary>
        public int MissingCount => Stages.Count(s => !s.IsKnown && !s.IsStageInactive);
        public bool HasMissing => MissingCount > 0;
        public string AddAllText => $"ضيف الـ {MissingCount} مرحلة الناقصة";

        /// <summary>مالوش ولا مهارة في المنتج ده (بيبان في وضع الإضافة بس)</summary>
        public bool IsUntouched => KnownCount == 0 && InactiveSkillCount == 0;

        [ObservableProperty]
        private bool _isExpanded;

        /// <summary>مطابق للبحث دلوقتي؟ (البحث في مهارات العامل نفسه)</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        /// <summary>بيتنادى بعد أي إضافة/إزالة مهارة عشان الأرقام على الكارت تتحدث</summary>
        public void RefreshCounters()
        {
            OnPropertyChanged(nameof(KnownCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(CoverageText));
            OnPropertyChanged(nameof(CoversWholeLine));
            OnPropertyChanged(nameof(InactiveSkillCount));
            OnPropertyChanged(nameof(HasInactiveSkills));
            OnPropertyChanged(nameof(InactiveSkillsText));
            OnPropertyChanged(nameof(MissingCount));
            OnPropertyChanged(nameof(HasMissing));
            OnPropertyChanged(nameof(AddAllText));
            OnPropertyChanged(nameof(IsUntouched));
        }
    }

    /// <summary>
    /// مرحلة واحدة جوّه كارت المنتج. بتتعرض حتى لو العامل مش بيعرفها —
    /// الفجوة نفسها معلومة مفيدة، والزرار اللي جنبها بيسدّها في مكانها.
    /// </summary>
    public partial class SkillStageItem : ObservableObject
    {
        public int StageId { get; init; }
        public int ProductId { get; init; }
        public string StageName { get; init; } = "";

        /// <summary>ترتيب المرحلة في خط الإنتاج (نفس الترتيب في شاشة المنتجات)</summary>
        public int Position { get; init; }

        /// <summary>المرحلة موقوفة — المهارة عليها مش هتنفع في أي رحلة إنتاج</summary>
        public bool IsStageInactive { get; init; }

        /// <summary>العامل بيعرف المرحلة دي؟ (بيتقلب بزرار جنبها)</summary>
        [ObservableProperty]
        private bool _isKnown;

        /// <summary>مطابق للبحث دلوقتي؟</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        // ------- التقييم -------

        /// <summary>تقييم المدير من 1 لـ 5 — بيتغيّر بالضغط على النجمة</summary>
        [ObservableProperty]
        private int _stars = SkillRatingService.DefaultStars;

        /// <summary>إنتاجه الفعلي ÷ الكوتة (0 = لسه مافيش قياس)</summary>
        public decimal MeasuredRatio { get; set; }

        /// <summary>عدد أيام الشغل اللي القياس اتبنى عليها</summary>
        public int MeasuredDays { get; set; }

        partial void OnStarsChanged(int value)
        {
            OnPropertyChanged(nameof(StarsLabel));
            OnPropertyChanged(nameof(RatingTooltip));
            OnPropertyChanged(nameof(HasGapWithReality));
            RefreshStarFlags();
        }

        /// <summary>وصف التقييم بالعربي (ممتاز / كويس جدًا / عادي ...)</summary>
        public string StarsLabel => SkillRatingService.StarsLabel(Stars);

        /// <summary>فيه قياس فعلي؟</summary>
        public bool HasMeasurement => MeasuredDays > 0;

        /// <summary>الأداء المقاس كنسبة ("115%")</summary>
        public string MeasuredText => HasMeasurement ? $"{MeasuredRatio * 100:0}%" : "";

        /// <summary>
        /// تقييم المدير بعيد عن الأداء الفعلي — بيتعلّم عشان يراجعه.
        /// ده اللي بيخلي المدير يشوف الفجوة من غير ما يفتح شاشة المراجعة.
        /// </summary>
        public bool HasGapWithReality =>
            HasMeasurement && SkillRatingService.StarsForRatio(MeasuredRatio) != Stars;

        public string RatingTooltip => HasMeasurement
            ? $"تقييمك: {StarsLabel} ({Stars}/5)\nإنتاجه الفعلي: {MeasuredText} من الكوتة على مدار {MeasuredDays} يوم"
            : $"تقييمك: {StarsLabel} ({Stars}/5)\nلسه مافيش إنتاج كفاية للقياس";

        // ------- حالة كل نجمة (للعرض والضغط) -------
        // خمس خصائص منفصلة عشان الـ XAML يربط عليها مباشرة من غير
        // محوّلات ولا قوايم متداخلة

        public bool Star1 => Stars >= 1;
        public bool Star2 => Stars >= 2;
        public bool Star3 => Stars >= 3;
        public bool Star4 => Stars >= 4;
        public bool Star5 => Stars >= 5;

        private void RefreshStarFlags()
        {
            OnPropertyChanged(nameof(Star1));
            OnPropertyChanged(nameof(Star2));
            OnPropertyChanged(nameof(Star3));
            OnPropertyChanged(nameof(Star4));
            OnPropertyChanged(nameof(Star5));
        }
    }

    /// <summary>
    /// كارت أسبوع واحد في هستوري العامل. مقفول بيوري الرقمين اللي بيتسألوا
    /// عنهم فعلاً (الصافي والأجر)، وبيتفتح على التفاصيل كصفوف — قبل كده كانت
    /// التفاصيل سطر نص واحد بيلف ("GRS/دبله: 9999 قطعة، GRS/رقبه: ...")
    /// ومستحيل تقرا منه رقم مرحلة معينة.
    /// </summary>
    public partial class WeekHistoryItem : ObservableObject
    {
        /// <summary>مدى الأسبوع بالتاريخ (يوم/شهر — يوم/شهر)</summary>
        public string WeekTitle { get; init; } = "";

        /// <summary>"الأسبوع الحالي" / "الأسبوع اللي فات" — فاضي لباقي الأسابيع</summary>
        public string RelativeLabel { get; init; } = "";
        public bool HasRelativeLabel => RelativeLabel.Length > 0;

        public decimal Produced { get; init; }
        public decimal AbsenceDeduction { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal Net { get; init; }

        /// <summary>أحسن عامل في الأسبوع ده</summary>
        public bool IsBest { get; init; }

        public string WageText { get; init; } = "";
        public bool HasWage => WageText.Length > 0;

        // الأرقام كنص منسّق — بدل ما XAML يعرض 25.0000 من الـ decimal الخام
        public string ProducedText => $"{Produced:0.##}";
        public string NetText => $"{Net:0.##}";
        public string AbsenceText => $"−{AbsenceDeduction:0.##}";
        public string PenaltyText => $"−{PenaltyDeduction:0.##}";

        public bool HasAbsence => AbsenceDeduction > 0;
        public bool HasPenaltyDeduction => PenaltyDeduction > 0;

        public ObservableCollection<WeekStageRow> Breakdown { get; init; } = new();
        public ObservableCollection<WeekPenaltyRow> Penalties { get; init; } = new();

        public bool HasBreakdown => Breakdown.Count > 0;
        public bool HasPenalties => Penalties.Count > 0;

        /// <summary>أسبوع مفيهوش أي شغل — بيتعرض مطفي ومبيتفتحش</summary>
        public bool IsEmptyWeek => Produced == 0 && !HasAbsence && !HasPenaltyDeduction;

        /// <summary>عدد المراحل اللي اشتغل عليها — بيبان على الكارت المقفول</summary>
        public string StagesCountText => Breakdown.Count == 1 ? "مرحلة واحدة" : $"{Breakdown.Count} مراحل";

        [ObservableProperty]
        private bool _isExpanded;
    }

    /// <summary>سطر إنتاج واحد جوّه كارت الأسبوع (منتج / مرحلة / قطع)</summary>
    public class WeekStageRow
    {
        public string ProductName { get; init; } = "";
        public string StageName { get; init; } = "";
        public int PieceCount { get; init; }

        public string PiecesText => $"{PieceCount:N0} قطعة";
    }

    /// <summary>سطر جزاء واحد جوّه كارت الأسبوع</summary>
    public class WeekPenaltyRow
    {
        public string Reason { get; init; } = "";
        public string DeductionName { get; init; } = "";
        public string DateText { get; init; } = "";
    }
}
