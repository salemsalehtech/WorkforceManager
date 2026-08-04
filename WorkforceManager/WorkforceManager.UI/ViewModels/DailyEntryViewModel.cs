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
    /// عقل شاشة التسجيل اليومي، وفيها 3 أقسام لنفس اليوم المختار:
    ///
    /// 1) رحلات الإنتاج: ممكن الشغل يكون على منتج أو أكتر في نفس اليوم —
    ///    كل منتج ليه "رحلة" مستقلة (FlowSessionViewModel): مراحله بترتيب
    ///    خط الإنتاج، توزيع العمال المؤهلين، نطاقات الإنتاج، معاينة
    ///    اليوميات، وحفظ مستقل. زرار "إضافة منتج" بيضيف رحلة جديدة.
    ///
    /// 2) الحضور: كل العمال النشطين بحالة حضور وحفظ جماعي (Upsert).
    /// 3) الجزاءات: تسجيل جزاء بسبب وخصم محدد، وقائمة جزاءات اليوم مع حذف.
    /// </summary>
    public partial class DailyEntryViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>كل المنتجات النشطة بمراحلها — بتتحمل مرة واحدة وتتشارك بين كل الرحلات</summary>
        private readonly List<ProductOption> _products = new();

        public DailyEntryViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;

            // خيارات الخصم الثابتة لقائمة الجزاءات
            DeductionOptions = new List<DeductionOption>
            {
                new(PenaltyDeduction.HalfDay),
                new(PenaltyDeduction.OneDay),
                new(PenaltyDeduction.ThreeDays),
                new(PenaltyDeduction.OneWeek)
            };
            SelectedDeduction = DeductionOptions[0];
        }

        // ------- اليوم المختار (مشترك بين الأقسام الثلاثة) -------

        [ObservableProperty]
        private DateTime _entryDate = DateTime.Today;

        partial void OnEntryDateChanged(DateTime value)
        {
            // تغيير اليوم بيعيد تحميل كل حاجة مرتبطة بيه (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(ReloadForDateAsync);
        }

        /// <summary>أول تحميل للشاشة: المنتجات + أول رحلة + الحضور + الجزاءات</summary>
        public async Task InitializeAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();

                var products = await productRepo.GetActiveWithStagesAsync();
                _products.Clear();
                foreach (var p in products)
                {
                    // المراحل بترتيب خط الإنتاج + رقم الترتيب المعروض (1، 2، 3...)
                    var stages = p.Stages
                        .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                        .Select((s, i) => new StageEntryOption
                        {
                            StageId = s.Id,
                            StageName = s.StageName,
                            PiecesPerWorkday = s.PiecesPerWorkday,
                            DisplayOrder = i + 1
                        }).ToList();

                    _products.Add(new ProductOption { ProductId = p.Id, Name = p.Name, Stages = stages });
                }
            }

            // أول رحلة جاهزة بأول منتج — الشاشة بتفتح شغالة على طول
            FlowSessions.Clear();
            var firstSession = CreateSession();
            firstSession.SelectedProduct = _products.FirstOrDefault();
            FlowSessions.Add(firstSession);

            await LoadDayRecordsAsync();
            await LoadAttendanceAsync();
            await LoadPenaltiesAsync();
            await LoadAdjustmentsAsync();
            await LoadClosureStateAsync();
        }

        private async Task ReloadForDateAsync()
        {
            // كل رحلة بتعيد تحميل "مسجل اليوم" بتاعها لليوم الجديد
            foreach (var session in FlowSessions)
                await session.ReloadAsync();

            await LoadDayRecordsAsync();
            await LoadAttendanceAsync();
            await LoadPenaltiesAsync();
            await LoadAdjustmentsAsync();
            await LoadClosureStateAsync();
        }

        /// <summary>بعد حفظ أي رحلة: الحضور التلقائي وسجلات اليوم بيظهروا فورًا</summary>
        private async Task OnFlowSavedAsync()
        {
            await LoadAttendanceAsync();
            await LoadDayRecordsAsync();
            await LoadClosureStateAsync();
        }

        // ======================= إقفال إنتاج اليوم =======================

        /// <summary>اليوم ده مقفول؟ (بيقفل التسجيل ويقلب الزرار لـ "فتح اليوم")</summary>
        [ObservableProperty]
        private bool _isDayClosed;

        /// <summary>ملخص الواقف — بيظهر جنب الزرار عشان المستخدم يعرف قبل ما يدوس</summary>
        [ObservableProperty]
        private string _carriedSummary = "";

        public bool HasCarriedWork => CarriedSummary.Length > 0;

        partial void OnCarriedSummaryChanged(string value) => OnPropertyChanged(nameof(HasCarriedWork));

        private async Task LoadClosureStateAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var closure = scope.ServiceProvider.GetRequiredService<DayClosureService>();
            var report = scope.ServiceProvider.GetRequiredService<DailyProductionReportService>();

            IsDayClosed = await closure.IsClosedAsync(EntryDate);

            var parked = await report.GetAllParkedAsync(EntryDate);
            var pieces = parked.Sum(p => p.ParkedPieces);
            CarriedSummary = pieces == 0
                ? ""
                : $"{pieces:N0} قطعة مستنية في {parked.Count} منتج";
        }

        /// <summary>
        /// يقفل اليوم بعد مراجعة أرقامه، أو يفتحه تاني لو كان مقفول.
        /// </summary>
        [RelayCommand]
        private async Task ToggleDayClosureAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DayClosureService>();

            try
            {
                if (IsDayClosed)
                {
                    var confirm = MessageBox.Show(
                        $"فتح إنتاج يوم {EntryDate:yyyy/MM/dd} تاني؟\nهيرجع ينفع يتسجل عليه إنتاج ويتعدّل.",
                        "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;

                    await service.ReopenAsync(EntryDate);
                    await LoadClosureStateAsync();
                    return;
                }

                var preview = await service.PreviewAsync(EntryDate);
                var dialog = new DayClosureDialog(preview) { Owner = Application.Current.MainWindow };
                if (dialog.ShowDialog() != true) return;

                await service.CloseAsync(EntryDate);
                await LoadClosureStateAsync();

                MessageBox.Show(
                    $"اتقفل إنتاج يوم {EntryDate:yyyy/MM/dd}.\n" +
                    (preview.ParkedPieces > 0
                        ? $"{preview.ParkedPieces:N0} قطعة لسه مستنية في الخط — هتلاقي أرقامها على المراحل بكرة."
                        : "مفيش شغل مستني في الخط."),
                    "تم القفل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "مش هينفع", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ======================= قسم رحلات الإنتاج (منتج أو أكتر في اليوم) =======================

        public ObservableCollection<FlowSessionViewModel> FlowSessions { get; } = new();

        /// <summary>
        /// رحلة جديدة مربوطة بيوم الشاشة وتحديث الحضور والسجلات بعد حفظها.
        /// بتاخد كمان طريقة توصلها لباقي الرحلات المفتوحة، عشان تحذير
        /// "العامل مكلّف بحاجة تانية" يشوف اللي لسه متحفظش في رحلات
        /// المنتجات التانية مش المحفوظ بس.
        /// </summary>
        private FlowSessionViewModel CreateSession() =>
            new(_scopeFactory, _products, () => EntryDate, OnFlowSavedAsync, () => FlowSessions);

        /// <summary>إضافة منتج تاني للشغل عليه في نفس اليوم (رحلة جديدة فاضية بيختار منتجها)</summary>
        [RelayCommand]
        private void AddFlowSession()
        {
            FlowSessions.Add(CreateSession());
        }

        [RelayCommand]
        private void RemoveFlowSession(FlowSessionViewModel? session)
        {
            if (session is null) return;

            // لازم تفضل رحلة واحدة على الأقل — الشاشة من غير ولا رحلة ملهاش معنى
            if (FlowSessions.Count == 1)
            {
                MessageBox.Show("لازم تفضل رحلة منتج واحدة على الأقل — لو عايز منتج مختلف غيّره من القائمة",
                    "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // تأكيد بس لو المستخدم كتب حاجة فيها (عشان ميخسرش شغله بضغطة غلط)
            if (session.HasUserInput &&
                MessageBox.Show($"إزالة رحلة \"{session.SelectedProduct?.Name ?? "بدون منتج"}\"؟ اللي اتكتب فيها هيضيع (اللي اتحفظ قبل كده محفوظ عادي).",
                    "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            FlowSessions.Remove(session);
        }

        // ======================= قسم سجلات اليوم (التصحيح) =======================

        /// <summary>كل سجلات الإنتاج المحفوظة في اليوم المختار — للمراجعة والتصحيح</summary>
        public ObservableCollection<DayRecordRow> DayRecords { get; } = new();

        // ------- ملخص اليوم (فوق تبويب تسجيل الإنتاج) -------

        /// <summary>إجمالي القطع المسجلة النهارده على كل المنتجات</summary>
        [ObservableProperty]
        private int _dayTotalPieces;

        /// <summary>إجمالي اليوميات المحسوبة من الإنتاج المسجل</summary>
        [ObservableProperty]
        private decimal _dayTotalWorkdays;

        /// <summary>عدد العمال اللي ليهم إنتاج مسجل النهارده</summary>
        [ObservableProperty]
        private int _dayWorkersCount;

        /// <summary>عدد المنتجات اللي اتسجل عليها شغل النهارده</summary>
        [ObservableProperty]
        private int _dayProductsCount;

        /// <summary>مفيش أي إنتاج مسجل لسه (بيخفي أرقام الملخص)</summary>
        public bool DayHasNoProduction => DayTotalPieces == 0;

        partial void OnDayTotalPiecesChanged(int value) => OnPropertyChanged(nameof(DayHasNoProduction));

        private async Task LoadDayRecordsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var productionRepo = scope.ServiceProvider.GetRequiredService<IDailyProductionRepository>();

            var records = await productionRepo.GetByDateAsync(EntryDate);

            // ملخص اليوم من نفس السجلات المحمّلة — من غير أي استعلام زيادة
            DayTotalPieces = records.Sum(r => r.PieceCount);
            DayTotalWorkdays = Math.Round(records.Sum(r => r.WorkdaysCompleted), 2);
            DayWorkersCount = records.Select(r => r.WorkerId).Distinct().Count();
            DayProductsCount = records.Select(r => r.ProductionStage.ProductId).Distinct().Count();

            DayRecords.Clear();
            foreach (var r in records.OrderBy(r => r.Worker.FullName).ThenBy(r => r.Id))
            {
                DayRecords.Add(new DayRecordRow
                {
                    RecordId = r.Id,
                    WorkerName = r.Worker.FullName,
                    StageDisplay = $"{r.ProductionStage.Product.Name} / {r.ProductionStage.StageName}",
                    PieceCount = r.PieceCount,
                    QuotaAtEntry = r.PiecesPerWorkdayAtEntry,
                    Workdays = r.WorkdaysCompleted
                });
            }
        }

        [RelayCommand]
        private async Task EditDayRecordAsync(DayRecordRow? row)
        {
            if (row is null) return;

            var dialog = new Views.EditProductionDialog { Owner = Application.Current.MainWindow };
            dialog.LoadRecord(row.WorkerName, row.StageDisplay, row.PieceCount);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workdayService = scope.ServiceProvider.GetRequiredService<WorkdayCalculationService>();
                await workdayService.UpdateProductionAsync(row.RecordId, dialog.NewPieceCount);

                // إعادة تحميل كل حاجة مرتبطة باليوم — الأرقام بتتصحح في كل مكان فورًا
                await ReloadForDateAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في التصحيح", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task DeleteDayRecordAsync(DayRecordRow? row)
        {
            if (row is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

                // النافذة المشتركة بتجمع كلمة السر والسبب — والتحقق نفسه
                // في الخدمة، فمفيش شاشة بتقارن كلمة سر بنفسها
                var input = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "حذف سجل إنتاج",
                    $"{row.WorkerName} — {row.StageDisplay} ({row.PieceCount} قطعة). يومياته هتتخصم من حسابه.",
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var workdayService = scope.ServiceProvider.GetRequiredService<WorkdayCalculationService>();
                var result = await workdayService.DeleteProductionAsync(
                    row.RecordId, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    MessageBox.Show(result.Message, "مش هينفع", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await ReloadForDateAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في الحذف", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ======================= قسم الحضور (موحّد: بالقطعة + بالساعة) =======================
        //
        // تبويب "العمال بالساعة" المنفصل اتشال — العمال بالساعة بقوا في نفس
        // قائمة الحضور، وبيتسجل شغلهم من اختصارات الشيفت اللي على سطرهم.

        /// <summary>
        /// قائمة الحضور الموحّدة: العمال بالقطعة والعمال بالساعة في مكان
        /// واحد، كل واحد بحالاته المناسبة لنوعه (والعامل بالساعة بيزود
        /// عليه اختصارات الشيفت).
        /// </summary>
        public ObservableCollection<AttendanceRow> AttendanceRows { get; } = new();

        /// <summary>الصفوف المعروضة فعليًا بعد البحث (البحث بيفلتر العرض بس، مش بيمسح أي اختيار)</summary>
        public ObservableCollection<AttendanceRow> VisibleAttendanceRows { get; } = new();


        [ObservableProperty]
        private string _attendanceSearch = string.Empty;

        partial void OnAttendanceSearchChanged(string value) => ApplyAttendanceFilter();

        /// <summary>
        /// القسم المفتوح حاليًا من شريط الملخص. الفلتر ده بيشتغل مع البحث
        /// مش بدله — تقدر تفتح "بدون إذن" وتدوّر باسم جواه.
        /// </summary>
        [ObservableProperty]
        private AttendanceFilter _activeAttendanceFilter = AttendanceFilter.All;

        partial void OnActiveAttendanceFilterChanged(AttendanceFilter value)
        {
            ApplyAttendanceFilter();
            RefreshFilterFlags();
        }

        /// <summary>
        /// الضغط على عدّاد في شريط الملخص بيفتح قسمه. الضغط على القسم
        /// المفتوح تاني بيرجّع الكل — فالزرار نفسه بيفتح ويقفل.
        /// </summary>
        [RelayCommand]
        private void SetAttendanceFilter(AttendanceFilter filter)
        {
            ActiveAttendanceFilter = ActiveAttendanceFilter == filter && filter != AttendanceFilter.All
                ? AttendanceFilter.All
                : filter;
        }

        private void ApplyAttendanceFilter()
        {
            var query = AttendanceSearch?.Trim() ?? "";

            IEnumerable<AttendanceRow> matches = ActiveAttendanceFilter switch
            {
                AttendanceFilter.Present => AttendanceRows.Where(r => r.SelectedStatus == AttendanceStatus.Present),
                AttendanceFilter.Excused => AttendanceRows.Where(r => r.SelectedStatus == AttendanceStatus.AbsentWithPermission),
                AttendanceFilter.Unexcused => AttendanceRows.Where(r => r.SelectedStatus == AttendanceStatus.AbsentWithoutPermission),
                AttendanceFilter.Unset => AttendanceRows.Where(r => r.SelectedStatus is null),
                _ => AttendanceRows
            };

            if (!string.IsNullOrEmpty(query))
                matches = matches.Where(r => r.FullName.Contains(query, StringComparison.OrdinalIgnoreCase));

            VisibleAttendanceRows.Clear();
            foreach (var row in matches) VisibleAttendanceRows.Add(row);

            OnPropertyChanged(nameof(NoAttendanceMatches));
            OnPropertyChanged(nameof(EmptyStateText));
        }

        public bool NoAttendanceMatches => VisibleAttendanceRows.Count == 0 && AttendanceRows.Count > 0;

        /// <summary>رسالة الفراغ بتفرّق بين "القسم فاضي" و"البحث ملقاش" عشان متلخبطش المستخدم</summary>
        public string EmptyStateText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AttendanceSearch))
                    return "مفيش عامل بالاسم ده في القسم ده";

                return ActiveAttendanceFilter switch
                {
                    AttendanceFilter.Present => "مفيش حد متسجل حاضر لحد دلوقتي",
                    AttendanceFilter.Excused => "مفيش حد غايب بإذن النهارده",
                    AttendanceFilter.Unexcused => "مفيش حد غايب بدون إذن النهارده",
                    AttendanceFilter.Unset => "تمام — كل العمال اتسجلت حالتهم",
                    _ => "مفيش عمال في القائمة"
                };
            }
        }

        // ------- حالة كل زرار في شريط الملخص (عشان المفتوح يبان مميّز) -------

        public bool IsFilterAll => ActiveAttendanceFilter == AttendanceFilter.All;
        public bool IsFilterPresent => ActiveAttendanceFilter == AttendanceFilter.Present;
        public bool IsFilterExcused => ActiveAttendanceFilter == AttendanceFilter.Excused;
        public bool IsFilterUnexcused => ActiveAttendanceFilter == AttendanceFilter.Unexcused;
        public bool IsFilterUnset => ActiveAttendanceFilter == AttendanceFilter.Unset;

        private void RefreshFilterFlags()
        {
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterPresent));
            OnPropertyChanged(nameof(IsFilterExcused));
            OnPropertyChanged(nameof(IsFilterUnexcused));
            OnPropertyChanged(nameof(IsFilterUnset));
        }

        // ------- عدّادات الملخص اللي فوق القائمة -------

        public int PresentCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.Present);
        public int ExcusedCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.AbsentWithPermission);
        public int UnexcusedCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.AbsentWithoutPermission);
        public int UnsetCount => AttendanceRows.Count(r => r.SelectedStatus is null);
        public int TotalWorkersCount => AttendanceRows.Count;

        /// <summary>الخصم المتوقع لو حفظت دلوقتي — تحذير قبل الحفظ مش بعده</summary>
        public string PendingPenaltyText => UnexcusedCount == 0
            ? ""
            : $"هيتسجل {UnexcusedCount} جزاء غياب تلقائي (نص يومية لكل واحد)";

        private void RefreshAttendanceSummary()
        {
            OnPropertyChanged(nameof(PresentCount));
            OnPropertyChanged(nameof(ExcusedCount));
            OnPropertyChanged(nameof(UnexcusedCount));
            OnPropertyChanged(nameof(UnsetCount));
            OnPropertyChanged(nameof(TotalWorkersCount));
            OnPropertyChanged(nameof(PendingPenaltyText));

            // لو فيه قسم مفتوح، لازم القائمة تفضل متطابقة مع رقم العدّاد:
            // عامل غيّرت حالته وهو في قسم "لسه مفيش" بيخرج من القسم فورًا،
            // فالرقم واللي تحته دايمًا بيقولوا نفس الحاجة.
            if (ActiveAttendanceFilter != AttendanceFilter.All)
                ApplyAttendanceFilter();
        }

        private async Task LoadAttendanceAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
            var attendanceRepo = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
            var hourlyService = scope.ServiceProvider.GetRequiredService<HourlyWorkdayService>();
            var automation = scope.ServiceProvider.GetRequiredService<AttendanceAutomationService>();

            var workers = await workerRepo.GetActiveWithSkillsAsync();
            var savedStatuses = (await attendanceRepo.GetByDateAsync(EntryDate))
                .ToDictionary(a => a.WorkerId, a => a.Status);

            // "مين اشتغل النهارده" من القاعدة المشتركة (مش استنتاج محلي)
            var workersWithWork = await automation.GetWorkersWithLoggedWorkAsync(EntryDate);

            var hourlyLogs = (await hourlyService.GetByDateAsync(EntryDate))
                .ToDictionary(h => h.WorkerId, h => h.EndHour24);

            AttendanceRows.Clear();
            foreach (var w in workers.OrderBy(w => w.IsHourly).ThenBy(w => w.FullName))
            {
                savedStatuses.TryGetValue(w.Id, out var saved);
                var hasSaved = savedStatuses.ContainsKey(w.Id);
                var hasWork = workersWithWork.Contains(w.Id);

                var row = new AttendanceRow(
                    w.Id,
                    w.FullName,
                    w.IsHourly,
                    w.IsHourly ? w.HourlyRole!.Value.ToArabicName() : "بالقطعة")
                {
                    HasLoggedWork = hasWork,
                    SavedStatus = hasSaved ? saved : null
                };

                // المحفوظ بيكسب دايمًا؛ ولو مفيش محفوظ والعامل له شغل
                // مسجّل → "حاضر" تلقائي وظاهر قدام المستخدم قبل ما يحفظ
                var initialStatus = hasSaved
                    ? saved
                    : hasWork ? AttendanceStatus.Present : (AttendanceStatus?)null;

                row.SelectStatusSilently(initialStatus);

                if (w.IsHourly && hourlyLogs.TryGetValue(w.Id, out var endHour))
                    row.SelectShiftSilently(endHour);

                // أي تغيير في السطر بيحدّث عدّادات الملخص فورًا
                row.StatusChanged += RefreshAttendanceSummary;

                AttendanceRows.Add(row);
            }

            ApplyAttendanceFilter();
            RefreshAttendanceSummary();
        }

        [RelayCommand]
        private async Task SaveAttendanceAsync()
        {
            // الصفوف اللي عليها حالة محددة (اللي من غير تحديد بنسيبها زي ما هي)
            var rowsToSave = AttendanceRows.Where(r => r.SelectedStatus is not null).ToList();

            if (rowsToSave.Count == 0)
            {
                MessageBox.Show("مفيش أي حالة حضور محددة للحفظ", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // تحذير مبكر: عامل له شغل مسجّل واتعلّم غياب. الخدمة هترفض
            // برضه (قاعدة حماية)، بس الرسالة هنا بتوضّح السبب قبل الحفظ
            var conflicting = rowsToSave
                .Where(r => r.HasLoggedWork && r.SelectedStatus != AttendanceStatus.Present)
                .ToList();

            if (conflicting.Count > 0)
            {
                var names = string.Join("\n", conflicting.Select(r => $"  • {r.FullName}"));
                MessageBox.Show(
                    "العمال دول ليهم شغل مسجّل النهارده ومتعلّم عليهم غياب:\n" + names +
                    "\n\nعامل شغل مينفعش يتسجل غايب في نفس اليوم. لو فعلاً كانوا غايبين، " +
                    "امسح شغلهم الأول من تبويب \"سجلات اليوم\".",
                    "تعارض: شغل مسجّل مع غياب", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                AttendanceSaveResultDto result;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var attendanceService = scope.ServiceProvider.GetRequiredService<AttendanceService>();
                    var hourlyService = scope.ServiceProvider.GetRequiredService<HourlyWorkdayService>();

                    // شغل العمال بالساعة الأول (بيسجل حضور تلقائي لو مفيش)،
                    // وبعدين الحضور بيكتب الحالة النهائية اللي المستخدم اختارها
                    foreach (var row in rowsToSave.Where(r => r.IsHourly && r.SelectedEndHour is not null))
                        await hourlyService.RecordHourlyWorkAsync(row.WorkerId, EntryDate, row.SelectedEndHour!.Value);

                    var entries = rowsToSave.Select(r => (r.WorkerId, Status: r.SelectedStatus!.Value));

                    // حفظ جماعي + مصالحة جزاءات الغياب في معاملة واحدة
                    result = await attendanceService.RecordAttendanceBatchAsync(EntryDate, entries);
                }

                var penaltyLines = "";
                if (result.AutoPenaltiesCreated > 0)
                    penaltyLines += $"\n⚠ اتسجل {result.AutoPenaltiesCreated} جزاء غياب تلقائي (نص يومية لكل واحد)";
                if (result.AutoPenaltiesRemoved > 0)
                    penaltyLines += $"\n✔ اتشال {result.AutoPenaltiesRemoved} جزاء غياب تلقائي (الحالة اتغيّرت)";

                MessageBox.Show(
                    $"تم حفظ حضور {result.SavedCount} عامل بتاريخ {EntryDate:yyyy/MM/dd}{penaltyLines}",
                    "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadAttendanceAsync();
                await LoadPenaltiesAsync(); // الجزاءات التلقائية تظهر/تختفي فورًا
            }
            catch (InvalidOperationException ex)
            {
                // قاعدة الحماية: غياب لعامل له شغل في نفس اليوم بيترفض برسالة بأسماء العمال
                MessageBox.Show(ex.Message, "تعارض في البيانات", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadAttendanceAsync(); // إرجاع القائمة للحالة المحفوظة الفعلية
            }
        }

        // ======================= قسم الجزاءات =======================

        public List<DeductionOption> DeductionOptions { get; }

        [ObservableProperty]
        private AttendanceRow? _penaltyWorker; // بنستخدم نفس صفوف الحضور كقائمة اختيار العامل

        [ObservableProperty]
        private string _penaltyReason = string.Empty;

        [ObservableProperty]
        private DeductionOption? _selectedDeduction;

        public ObservableCollection<PenaltyRow> DayPenalties { get; } = new();

        private async Task LoadPenaltiesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();

            var penalties = await penaltyService.GetPenaltiesByDateAsync(EntryDate);
            DayPenalties.Clear();
            foreach (var p in penalties)
            {
                DayPenalties.Add(new PenaltyRow
                {
                    PenaltyId = p.Id,
                    WorkerName = p.Worker.FullName,
                    Reason = p.Reason,
                    DeductionName = p.Deduction.ToArabicName()
                });
            }
        }

        [RelayCommand]
        private async Task AddPenaltyAsync()
        {
            if (PenaltyWorker is null)
            {
                MessageBox.Show("اختار العامل الأول", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(PenaltyReason))
            {
                MessageBox.Show("اكتب سبب الجزاء", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (SelectedDeduction is null) return;

            using var scope = _scopeFactory.CreateScope();
            var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();
            await penaltyService.RecordPenaltyAsync(
                PenaltyWorker.WorkerId, EntryDate, PenaltyReason, SelectedDeduction.Value);

            // تفريغ الفورم وإعادة تحميل قائمة اليوم
            PenaltyReason = string.Empty;
            PenaltyWorker = null;
            await LoadPenaltiesAsync();
        }

        [RelayCommand]
        private async Task RemovePenaltyAsync(PenaltyRow? row)
        {
            if (row is null) return;

            if (MessageBox.Show($"حذف جزاء \"{row.Reason}\" عن {row.WorkerName}؟",
                    "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();
            await penaltyService.RemovePenaltyAsync(row.PenaltyId);
            await LoadPenaltiesAsync();
        }

        // ======================= قسم السلف والحوافز =======================

        /// <summary>خيارات نوع الحركة (سلفة/حافز) للقائمة المنسدلة</summary>
        public List<AdjustmentTypeOption> AdjustmentTypeOptions { get; } = new()
        {
            new(WageAdjustmentType.Advance),
            new(WageAdjustmentType.Bonus)
        };

        [ObservableProperty]
        private AttendanceRow? _adjustmentWorker; // نفس صفوف الحضور كقائمة اختيار العامل

        [ObservableProperty]
        private AdjustmentTypeOption? _selectedAdjustmentType;

        [ObservableProperty]
        private string _adjustmentAmount = string.Empty;

        [ObservableProperty]
        private string _adjustmentNote = string.Empty;

        public ObservableCollection<AdjustmentRow> DayAdjustments { get; } = new();

        private async Task LoadAdjustmentsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WageAdjustmentService>();

            var adjustments = await service.GetByDateAsync(EntryDate);
            DayAdjustments.Clear();
            foreach (var a in adjustments)
            {
                DayAdjustments.Add(new AdjustmentRow
                {
                    AdjustmentId = a.Id,
                    WorkerName = a.Worker.FullName,
                    TypeName = a.Type.ToArabicName(),
                    // السلفة حمرا (خصم) والحافز أخضر (إضافة) — تمييز بصري سريع
                    TypeColor = a.Type == WageAdjustmentType.Bonus ? "#0B6E4F" : "#B00020",
                    AmountText = $"{a.AmountEgp:N0} ج",
                    Note = a.Note ?? ""
                });
            }
        }

        [RelayCommand]
        private async Task AddAdjustmentAsync()
        {
            if (AdjustmentWorker is null)
            {
                MessageBox.Show("اختار العامل الأول", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (SelectedAdjustmentType is null)
            {
                MessageBox.Show("اختار النوع (سلفة/حافز)", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!decimal.TryParse(AdjustmentAmount, out var amount) || amount <= 0)
            {
                MessageBox.Show("اكتب مبلغ صحيح أكبر من صفر", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WageAdjustmentService>();
            await service.RecordAdjustmentAsync(
                AdjustmentWorker.WorkerId, EntryDate, SelectedAdjustmentType.Value, amount, AdjustmentNote);

            // تفريغ الفورم وإعادة تحميل قائمة اليوم
            AdjustmentAmount = string.Empty;
            AdjustmentNote = string.Empty;
            AdjustmentWorker = null;
            await LoadAdjustmentsAsync();
        }

        [RelayCommand]
        private async Task RemoveAdjustmentAsync(AdjustmentRow? row)
        {
            if (row is null) return;

            if (MessageBox.Show($"حذف {row.TypeName} ({row.AmountText}) عن {row.WorkerName}؟",
                    "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WageAdjustmentService>();
            await service.RemoveAdjustmentAsync(row.AdjustmentId);
            await LoadAdjustmentsAsync();
        }
    }

    // ======================= نماذج العرض المشتركة للشاشة =======================

    /// <summary>منتج في قائمة الاختيار مع مراحله المرتبة الجاهزة</summary>
    public class ProductOption
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public List<StageEntryOption> Stages { get; init; } = new();
    }

    /// <summary>مرحلة في قوائم الاختيار (النطاقات) — برقم ترتيبها في خط الإنتاج</summary>
    public class StageEntryOption
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";
        public int PiecesPerWorkday { get; init; }
        public int DisplayOrder { get; init; }

        /// <summary>الاسم المعروض في قوائم "من/إلى": الترتيب + الاسم</summary>
        public string Display => $"{DisplayOrder}. {StageName}";
    }

    /// <summary>سجل إنتاج واحد في تبويب "سجلات اليوم" — للمراجعة والتصحيح</summary>
    public class DayRecordRow
    {
        public int RecordId { get; init; }
        public string WorkerName { get; init; } = "";
        public string StageDisplay { get; init; } = "";
        public int PieceCount { get; init; }
        public int QuotaAtEntry { get; init; }
        public decimal Workdays { get; init; }
    }

    /// <summary>خيار وقت انتهاء الشغل في قائمة العامل بالساعة</summary>
    /// <summary>
    /// زرار حالة واحد في سطر الحضور. الاختيار مانع للجمع (حالة واحدة
    /// لليوم — B1): تعليم واحد بيشيل التعليم عن الباقي، وتعليمه تاني
    /// بيلغي التسجيل ويرجّع السطر لـ"بدون تسجيل".
    /// </summary>
    public partial class AttendanceStatusChoice : ObservableObject
    {
        private readonly AttendanceRow _row;

        public AttendanceStatusChoice(AttendanceRow row, AttendanceStatus status)
        {
            _row = row;
            Status = status;
            Display = status.ToArabicName();
            AccentColor = AttendanceVisuals.ColorFor(status);
            Icon = AttendanceVisuals.IconFor(status);
        }

        public AttendanceStatus Status { get; }
        public string Display { get; }

        /// <summary>
        /// لون الحالة لما تتعلّم (أخضر حاضر / أصفر بإذن / أحمر بدون إذن).
        /// نص hex زي <c>AdjustmentRow.TypeColor</c> — WPF بيحوّله لفرشاة لوحده.
        /// </summary>
        public string AccentColor { get; }

        /// <summary>أيقونة MaterialDesign اللي بتوصف الحالة (بتتقري أسرع من النص)</summary>
        public string Icon { get; }

        /// <summary>بيوقف إبلاغ السطر وإحنا بنعدّل الاختيار برمجيًا (منع تكرار لا نهائي)</summary>
        private bool _suppressNotify;

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            if (_suppressNotify) return;
            _row.OnChoiceToggled(this, value);
        }

        /// <summary>تعليم/إلغاء من غير ما نرجّع نداء للسطر (بيستخدمه السطر وهو بيوحّد الاختيار)</summary>
        internal void SetSelectedSilently(bool selected)
        {
            _suppressNotify = true;
            try { IsSelected = selected; }
            finally { _suppressNotify = false; }
        }
    }

    /// <summary>
    /// اختصار شيفت للعامل بالساعة (شيفت عادي / لحد 8م / لحد 12).
    /// نفس منطق الاختيار المانع للجمع بتاع الحالات — شيفت واحد بس في اليوم.
    /// </summary>
    public partial class ShiftChoice : ObservableObject
    {
        private readonly AttendanceRow _row;

        public ShiftChoice(AttendanceRow row, int endHour24, string label)
        {
            _row = row;
            EndHour24 = endHour24;
            Display = $"{label} ({HourlyWorkdayService.ComputeWorkdays(endHour24)} يومية)";
        }

        public int EndHour24 { get; }
        public string Display { get; }

        private bool _suppressNotify;

        [ObservableProperty]
        private bool _isSelected;

        partial void OnIsSelectedChanged(bool value)
        {
            if (_suppressNotify) return;
            _row.OnShiftToggled(this, value);
        }

        internal void SetSelectedSilently(bool selected)
        {
            _suppressNotify = true;
            try { IsSelected = selected; }
            finally { _suppressNotify = false; }
        }
    }

    /// <summary>
    /// سطر حضور لعامل واحد في القائمة الموحّدة (بالقطعة أو بالساعة).
    ///
    /// الحالات المعروضة بتيجي من <see cref="AttendanceStatusCatalog"/>
    /// حسب نوع العامل — مش مكتوبة هنا. والعامل بالساعة بيزود عليه
    /// اختصارات الشيفت عشان يتسجل شغله من نفس الشاشة من غير تبويب منفصل.
    /// </summary>
    public partial class AttendanceRow : ObservableObject
    {
        /// <summary>بيمنع التكرار اللانهائي وإحنا بنلغي تعليم باقي الاختيارات</summary>
        private bool _syncing;

        public AttendanceRow(int workerId, string fullName, bool isHourly, string roleText)
        {
            WorkerId = workerId;
            FullName = fullName;
            IsHourly = isHourly;
            RoleText = roleText;

            StatusChoices = AttendanceStatusCatalog.ForWorker(isHourly)
                .Select(status => new AttendanceStatusChoice(this, status))
                .ToList();

            ShiftChoices = isHourly
                ? HourlyWorkdayService.ShiftPresets
                    .Select(preset => new ShiftChoice(this, preset.EndHour24, preset.Label))
                    .ToList()
                : new List<ShiftChoice>();
        }

        public int WorkerId { get; }
        public string FullName { get; }

        /// <summary>عامل بالساعة؟ (بيحدد الحالات المعروضة وظهور اختصارات الشيفت)</summary>
        public bool IsHourly { get; }

        /// <summary>نوع العامل للعرض: "بالقطعة" أو دوره بالساعة (رص/جودة/تدريب)</summary>
        public string RoleText { get; }

        /// <summary>أول حرفين من الاسم — بيتعرضوا في الدايرة جنب كل عامل بدل صورة</summary>
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

        /// <summary>لون الشريط الجانبي للبطاقة = لون الحالة المختارة (رمادي لو لسه مفيش)</summary>
        public string StatusColor => AttendanceVisuals.ColorFor(SelectedStatus);

        public IReadOnlyList<AttendanceStatusChoice> StatusChoices { get; }
        public IReadOnlyList<ShiftChoice> ShiftChoices { get; }

        /// <summary>الحالة المختارة دلوقتي (null = بدون تسجيل)</summary>
        public AttendanceStatus? SelectedStatus =>
            StatusChoices.FirstOrDefault(c => c.IsSelected)?.Status;

        /// <summary>وقت انتهاء الشيفت المختار للعامل بالساعة (null = مفيش شغل ساعة النهارده)</summary>
        public int? SelectedEndHour =>
            ShiftChoices.FirstOrDefault(c => c.IsSelected)?.EndHour24;

        // ------- الحضور التلقائي من الشغل المسجّل -------

        /// <summary>
        /// العامل ده له شغل مسجّل النهارده (إنتاج على مراحل أو شغل ساعة).
        /// بيتحدد من القاعدة المشتركة في AttendanceAutomationService.
        /// </summary>
        public bool HasLoggedWork { get; init; }

        /// <summary>الشرح اللي بيظهر جنب العامل اللي اتعلّم "حاضر" تلقائيًا</summary>
        public string WorkNote => HasLoggedWork ? "له شغل مسجّل النهارده" : "";

        /// <summary>الحالة المحفوظة فعليًا في قاعدة البيانات (لمعرفة إذا كان فيه تعديل غير محفوظ)</summary>
        public AttendanceStatus? SavedStatus { get; init; }

        // ------- منطق الاختيار المانع للجمع -------

        internal void OnChoiceToggled(AttendanceStatusChoice toggled, bool isSelected)
        {
            if (_syncing) return;

            _syncing = true;
            try
            {
                // حالة واحدة بس في اليوم: تعليم واحدة بيشيل الباقي
                if (isSelected)
                    foreach (var other in StatusChoices.Where(c => !ReferenceEquals(c, toggled)))
                        other.SetSelectedSilently(false);
            }
            finally
            {
                _syncing = false;
            }

            RaiseStatusVisualsChanged();
        }

        internal void OnShiftToggled(ShiftChoice toggled, bool isSelected)
        {
            if (_syncing) return;

            _syncing = true;
            try
            {
                if (isSelected)
                {
                    foreach (var other in ShiftChoices.Where(c => !ReferenceEquals(c, toggled)))
                        other.SetSelectedSilently(false);

                    // اختيار شيفت معناه العامل اشتغل — يبقى حاضر بالبديهة
                    SelectStatusSilently(AttendanceStatus.Present);
                }
            }
            finally
            {
                _syncing = false;
            }

            OnPropertyChanged(nameof(SelectedEndHour));
            RaiseStatusVisualsChanged();
        }

        /// <summary>يحدد حالة من غير ما يشغّل منطق المزامنة (للتحميل الأولي والحضور التلقائي)</summary>
        public void SelectStatusSilently(AttendanceStatus? status)
        {
            foreach (var choice in StatusChoices)
                choice.SetSelectedSilently(choice.Status == status);

            RaiseStatusVisualsChanged();
        }

        /// <summary>يحدد شيفت من غير منطق المزامنة (للتحميل الأولي)</summary>
        public void SelectShiftSilently(int? endHour24)
        {
            foreach (var choice in ShiftChoices)
                choice.SetSelectedSilently(choice.EndHour24 == endHour24);

            OnPropertyChanged(nameof(SelectedEndHour));
        }

        /// <summary>فيه تعديل لسه متحفظش؟ (لتلوين السطر)</summary>
        public bool HasUnsavedChange => SelectedStatus != SavedStatus;

        /// <summary>
        /// بيبلّغ كل الخصائص اللي بتتغير مع الحالة (اللون + التعديل غير
        /// المحفوظ). مكان واحد عشان أي تغيير للحالة يحدّث البطاقة كاملة،
        /// ومنسناش خاصية.
        /// </summary>
        private void RaiseStatusVisualsChanged()
        {
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(HasUnsavedChange));
            StatusChanged?.Invoke();
        }

        /// <summary>بيتنادى مع أي تغيير حالة — الشاشة الأم بتحدّث عدّادات الملخص</summary>
        public event Action? StatusChanged;
    }

    /// <summary>خيار خصم جزاء في القائمة المنسدلة</summary>
    public class DeductionOption
    {
        public DeductionOption(PenaltyDeduction value) => Value = value;
        public PenaltyDeduction Value { get; }
        public string Display => Value.ToArabicName();
    }

    /// <summary>جزاء واحد في قائمة جزاءات اليوم</summary>
    public class PenaltyRow
    {
        public int PenaltyId { get; init; }
        public string WorkerName { get; init; } = "";
        public string Reason { get; init; } = "";
        public string DeductionName { get; init; } = "";
    }

    /// <summary>خيار نوع الحركة (سلفة/حافز) في القائمة المنسدلة</summary>
    public class AdjustmentTypeOption
    {
        public AdjustmentTypeOption(WageAdjustmentType value) => Value = value;
        public WageAdjustmentType Value { get; }
        public string Display => Value.ToArabicName();
    }

    /// <summary>حركة سلفة/حافز واحدة في قائمة اليوم</summary>
    public class AdjustmentRow
    {
        public int AdjustmentId { get; init; }
        public string WorkerName { get; init; } = "";
        public string TypeName { get; init; } = "";
        public string TypeColor { get; init; } = "#333333";
        public string AmountText { get; init; } = "";
        public string Note { get; init; } = "";
    }

    /// <summary>
    /// أقسام شريط ملخص الحضور. كل عدّاد في الشريط بيفتح القسم بتاعه،
    /// و<see cref="AttendanceFilter.All"/> معناها الشريط مقفول والكل ظاهر.
    /// </summary>
    public enum AttendanceFilter
    {
        All,
        Present,
        Excused,
        Unexcused,
        Unset
    }
}
