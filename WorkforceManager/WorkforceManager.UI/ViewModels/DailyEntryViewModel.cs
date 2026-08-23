using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
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

        /// <summary>
        /// الشاشة دي Singleton (شوف App.xaml.cs) عشان توزيع العمال على
        /// المراحل يفضل موجود لو المستخدم راح لشاشة تانية ورجع من غير
        /// ما يحفظ — قبل كده كل تنقّل كان بيبني الشاشة من جديد ويمسح أي
        /// توزيع لسه مش محفوظ. بس Singleton لوحدها معناها InitializeAsync
        /// (اللي View.Loaded بينادّيها في كل رجوع للشاشة) هتتنفذ تاني
        /// وتمسح FlowSessions من غير قصد — الحارس ده بيمنع ده: أول مرة
        /// بس هي اللي بتبني الرحلة الأولى، وبعدها الرجوع للشاشة مبيغيّرش
        /// حاجة إلا لو المستخدم غيّر التاريخ (OnEntryDateChanged) بنفسه.
        /// </summary>
        private bool _initialized;

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

        /// <summary>
        /// أول تحميل للشاشة: المنتجات + أول رحلة + الحضور + الجزاءات.
        /// View.Loaded بينادّيها في كل رجوع للشاشة (الشاشة Singleton)،
        /// فبترجع فورًا من غير ما تعمل حاجة لو سبق واتحمّلت — رجوع
        /// المستخدم للشاشة مش لازم يمسح توزيع لسه مش محفوظ.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;

            using (var scope = _scopeFactory.CreateScope())
            {
                var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();

                var products = await productRepo.GetActiveWithStagesAsync();
                _products.Clear();
                foreach (var p in products)
                {
                    // المراحل بترتيب خط الإنتاج + رقم الترتيب المعروض (1، 2، 3...).
                    // مرحلة الرص **موجودة هنا** كبطاقة عادية آخر الترتيب —
                    // بس مستبعدة من نطاقات القطع (شوف FlowSessionViewModel)
                    var stages = p.Stages
                        .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                        .Select((s, i) => new StageEntryOption
                        {
                            StageId = s.Id,
                            StageName = s.StageName,
                            PiecesPerWorkday = s.PiecesPerWorkday,
                            DisplayOrder = i + 1,
                            IsRackingStage = s.IsRackingStage
                        }).ToList();

                    _products.Add(new ProductOption
                    {
                        ProductId = p.Id, Name = p.Name, Stages = stages,
                        RackingWorkerId = p.RackingWorkerId
                    });
                }
            }

            // أول رحلة جاهزة بأول منتج — الشاشة بتفتح شغالة على طول
            FlowSessions.Clear();
            var firstSession = CreateSession();
            firstSession.SelectedProduct = _products.FirstOrDefault();
            FlowSessions.Add(firstSession);

            await LoadDaySummaryAsync();
            await LoadRecordsTabAsync();
            await LoadAttendanceAsync();
            await LoadPenaltiesAsync();
            await LoadAdjustmentsAsync();
            await LoadScrapAsync();
            await LoadClosureStateAsync();
        }

        private async Task ReloadForDateAsync()
        {
            // كل رحلة بتعيد تحميل "مسجل اليوم" بتاعها لليوم الجديد
            foreach (var session in FlowSessions)
                await session.ReloadAsync();

            await LoadDaySummaryAsync();
            await LoadRecordsTabAsync();
            await LoadAttendanceAsync();
            await LoadPenaltiesAsync();
            await LoadAdjustmentsAsync();
            await LoadScrapAsync();
            await LoadClosureStateAsync();
        }

        /// <summary>
        /// بيرجّع الشاشة لحالتها الأولى — لازم يتنادى عند تسجيل الخروج.
        /// الشاشة Singleton (شوف توثيق _initialized فوق)، فمن غير الدالة
        /// دي حساب إداري تاني بيدخل بعد كده كان هيلاقي رحلة إنتاج لسه
        /// من غير حفظ سابها الحساب اللي قبله — والبرنامج بالتصميم بيسمح
        /// بأكتر من حساب إداري يستخدموا نفس الجهاز في نفس الشيفت.
        /// </summary>
        public void ResetForNewSession()
        {
            FlowSessions.Clear();
            _initialized = false;
            EntryDate = DateTime.Today;
        }

        /// <summary>بعد حفظ أي رحلة: الحضور التلقائي وسجلات اليوم بيظهروا فورًا</summary>
        private async Task OnFlowSavedAsync()
        {
            await LoadAttendanceAsync();
            await LoadDaySummaryAsync();
            await LoadRecordsTabAsync();
            // الرحلة ممكن تكون سجّلت هالك (البرنامج بيسأل عن الفرق بعد الحفظ)
            await LoadScrapAsync();
            await LoadClosureStateAsync();
        }

        // ======================= إقفال إنتاج اليوم =======================

        /// <summary>اليوم ده مقفول؟ (بيقفل التسجيل ويقلب الزرار لـ "فتح اليوم")</summary>
        [ObservableProperty]
        private bool _isDayClosed;

        private async Task LoadClosureStateAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var closure = scope.ServiceProvider.GetRequiredService<DayClosureService>();

            IsDayClosed = await closure.IsClosedAsync(EntryDate);
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
                    var reopenGate = SensitiveActionDialog.Ask(
                        Application.Current.MainWindow,
                        "فتح إنتاج اليوم تاني",
                        $"هيرجع ينفع يتسجل إنتاج على يوم {EntryDate:yyyy/MM/dd} ويتعدّل.",
                        SensitiveActionKind.Save,
                        passwordRequired: true,
                        reasonRequired: false);

                    if (reopenGate is null) return;

                    await service.ReopenAsync(EntryDate, reopenGate.Password);
                    await LoadClosureStateAsync();
                    return;
                }

                var preview = await service.PreviewAsync(EntryDate);
                var dialog = new DayClosureDialog(preview) { Owner = Application.Current.MainWindow };
                if (dialog.ShowDialog() != true) return;

                // القفل بيوقف تسجيل اليوم كله — بوابة زي باقي العمليات
                // اللي بتغيّر حالة القسم
                var closeGate = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "قفل إنتاج اليوم",
                    $"بعد القفل مش هينفع يتسجل إنتاج جديد على يوم {EntryDate:yyyy/MM/dd}.",
                    SensitiveActionKind.Save,
                    passwordRequired: true,
                    reasonRequired: false);

                if (closeGate is null) return;

                await service.CloseAsync(EntryDate, closeGate.Password);
                await LoadClosureStateAsync();

                Notify.Info($"اتقفل إنتاج يوم {EntryDate:yyyy/MM/dd}.\n" +
                    $"{preview.CompletedPieces:N0} قطعة خلصت الخط، و{preview.StartedPieces:N0} دخلته.", "تم القفل");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
        }

        /// <summary>
        /// يشيل كل إنتاج اليوم — حذف ناعم بكلمة سر وسبب مكتوب.
        ///
        /// عملية واحدة مش حلقة على السجلات: كلمة السر بتتسأل مرة، وكل
        /// السجلات بتتشال في معاملة واحدة. البوابة والحذف الناعم والإشعار
        /// كلهم من الأنظمة المشتركة — مفيش نسخة تانية منهم هنا.
        /// </summary>
        [RelayCommand]
        private async Task DeleteDayAsync()
        {
            var input = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "حذف إنتاج اليوم كله",
                $"كل سجلات إنتاج يوم {EntryDate:yyyy/MM/dd} هتتشال.\n" +
                "السجلات مش بتتمسح فعليًا — بتفضل محفوظة بسببها ومين شالها، " +
                "بس بتختفي من كل الحسابات والأجور.",
                SensitiveActionKind.Delete,
                passwordRequired: true);

            if (input is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<WorkdayCalculationService>()
                    .DeleteProductionDayAsync(EntryDate, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    Notify.Warn(result.Message, "مش هينفع");
                    return;
                }

                await LoadDaySummaryAsync();
                await LoadRecordsTabAsync();
                await LoadClosureStateAsync();

                var note = result.PasswordNotConfigured
                    ? "\n\nملحوظة: مفيش كلمة سر عمليات متسجّلة — اتنفّذ من غير تحقق. اتظبطها من الإعدادات."
                    : "";

                Notify.Info($"اتشال إنتاج يوم {EntryDate:yyyy/MM/dd}." + note, "تم");
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
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
                Notify.Info("لازم تفضل رحلة منتج واحدة على الأقل — لو عايز منتج مختلف غيّره من القائمة", "تنبيه");
                return;
            }

            // تأكيد بس لو المستخدم كتب حاجة فيها (عشان ميخسرش شغله بضغطة غلط)
            if (session.HasUserInput &&
                !Notify.Ask($"إزالة رحلة \"{session.SelectedProduct?.Name ?? "بدون منتج"}\"؟ اللي اتكتب فيها هيضيع (اللي اتحفظ قبل كده محفوظ عادي).", "تأكيد"))
                return;

            FlowSessions.Remove(session);
        }

        // ======================= قسم سجلات اليوم (التصحيح) =======================

        /// <summary>
        /// كل سجلات الإنتاج المحفوظة في الفترة المعروضة (بعد فلتر المنتج
        /// لو مفعّل) — للمراجعة والتصحيح. الفترة دي مستقلة عن EntryDate
        /// عن قصد (شوف RecordsGrain تحت): تغيير يوم التسجيل فوق الشاشة
        /// كلها مش المفروض يقلب التبويب ده لمكان تاني كل مرة.
        /// </summary>
        public ObservableCollection<DayRecordRow> DayRecords { get; } = new();

        /// <summary>كل سجلات الفترة قبل فلتر المنتج — مصدر خيارات الفلتر، والفلترة نفسها من غير رجعة لقاعدة البيانات</summary>
        private readonly List<DayRecordRow> _allDayRecords = new();

        /// <summary>يوم/أسبوع/شهر لتبويب "سجلات اليوم" — نفس نمط تبويب "الإنتاج" في شاشة التقييم والمتابعة بالظبط</summary>
        [ObservableProperty]
        private ChartGrain _recordsGrain = ChartGrain.Day;

        partial void OnRecordsGrainChanged(ChartGrain value)
        {
            OnPropertyChanged(nameof(IsRecordsGrainDay));
            OnPropertyChanged(nameof(IsRecordsGrainWeek));
            OnPropertyChanged(nameof(IsRecordsGrainMonth));
            SafeAsync.Run(LoadRecordsTabAsync);
        }

        public bool IsRecordsGrainDay => RecordsGrain == ChartGrain.Day;
        public bool IsRecordsGrainWeek => RecordsGrain == ChartGrain.Week;
        public bool IsRecordsGrainMonth => RecordsGrain == ChartGrain.Month;

        [RelayCommand]
        private void SetRecordsGrain(string? key) => RecordsGrain = key switch
        {
            "week" => ChartGrain.Week,
            "month" => ChartGrain.Month,
            _ => ChartGrain.Day
        };

        /// <summary>أي يوم جوّه الأسبوع/الشهر المعروض — افتراضيًا النهارده</summary>
        [ObservableProperty]
        private DateTime _recordsDate = DateTime.Today;

        partial void OnRecordsDateChanged(DateTime value) => SafeAsync.Run(LoadRecordsTabAsync);

        /// <summary>وصف الفترة المعروضة ("الأسبوع من.." أو "شهر..") — فاضي في وضع اليوم لأن التاريخ نفسه كافي</summary>
        [ObservableProperty]
        private string _recordsPeriodLabel = "";

        /// <summary>منتجات فيها سجلات في الفترة المعروضة بس — "كل المنتجات" أولها دايمًا</summary>
        public ObservableCollection<RecordsProductOption> RecordsProductOptions { get; } = new();

        [ObservableProperty]
        private RecordsProductOption? _selectedRecordsProduct;

        partial void OnSelectedRecordsProductChanged(RecordsProductOption? value) => ApplyRecordsProductFilter();

        // ------- ملخص اليوم (فوق تبويب تسجيل الإنتاج) — دايمًا بتاريخ EntryDate -------

        /// <summary>
        /// القطع اللي خلصت الخط كامل النهارده = المنتج التام.
        ///
        /// **مش مجموع القطع على كل المراحل.** القطعة بتعدّي المراحل
        /// بالترتيب مش بالتوازي، فـ 5,000 قطعة على منتج من 11 مرحلة كانت
        /// بتطلع 55,000 — نفس القطعة متحسوبة 11 مرة.
        /// </summary>
        [ObservableProperty]
        private int _dayTotalPieces;

        /// <summary>القطع اللي دخلت أول مرحلة النهارده</summary>
        [ObservableProperty]
        private int _dayStartedPieces;

        /// <summary>
        /// شغل اتسجل النهارده بس مفيش منه حاجة خلصت الخط — الملخص بيقول
        /// "دخل الخط" بدل ما يقول صفر ويوحي إن اليوم فاضي.
        /// </summary>
        public bool DayHasOnlyStarted => DayTotalPieces == 0 && DayStartedPieces > 0;

        public string DayPiecesLabel => DayHasOnlyStarted
            ? "قطعة دخلت الخط النهارده"
            : "قطعة خلصت الخط النهارده";

        /// <summary>الرقم المعروض: التام، أو الداخل لو مفيش تام</summary>
        public int DayHeadlinePieces => DayTotalPieces > 0 ? DayTotalPieces : DayStartedPieces;

        /// <summary>إجمالي اليوميات المحسوبة من الإنتاج المسجل</summary>
        [ObservableProperty]
        private decimal _dayTotalWorkdays;

        /// <summary>عدد العمال اللي ليهم إنتاج مسجل النهارده</summary>
        [ObservableProperty]
        private int _dayWorkersCount;

        /// <summary>عدد المنتجات اللي اتسجل عليها شغل النهارده</summary>
        [ObservableProperty]
        private int _dayProductsCount;

        /// <summary>
        /// هالك النهارده في ملخص اليوم.
        ///
        /// بيظهر بس لما يكون فيه هالك فعلاً (<see cref="DayHasScrap"/>) —
        /// "صفر هالك" مش معلومة وبياخد مساحة من الأرقام اللي بتتقري.
        /// </summary>
        [ObservableProperty]
        private int _dayScrapPieces;

        public bool DayHasScrap => DayScrapPieces > 0;

        partial void OnDayScrapPiecesChanged(int value) => OnPropertyChanged(nameof(DayHasScrap));

        /// <summary>
        /// مفيش أي إنتاج مسجل في EntryDate لسه (بيخفي أرقام الملخص).
        /// **مش من DayRecords** عن قصد — دي بقت مستقلة (فترة تانية
        /// محتمل)، فمصدرها عدد سجلات EntryDate الخام نفسه.
        /// </summary>
        private int _entryDayRecordCount;
        public bool DayHasNoProduction => _entryDayRecordCount == 0;

        private void RefreshDaySummaryFlags()
        {
            OnPropertyChanged(nameof(DayHasNoProduction));
            OnPropertyChanged(nameof(DayHasOnlyStarted));
            OnPropertyChanged(nameof(DayPiecesLabel));
            OnPropertyChanged(nameof(DayHeadlinePieces));
            OnPropertyChanged(nameof(DayHasScrap));
        }

        partial void OnDayTotalPiecesChanged(int value) => RefreshDaySummaryFlags();
        partial void OnDayStartedPiecesChanged(int value) => RefreshDaySummaryFlags();

        /// <summary>ملخص الأرقام فوق تبويب "تسجيل الإنتاج" — دايمًا يوم EntryDate بالظبط</summary>
        private async Task LoadDaySummaryAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var productionRepo = scope.ServiceProvider.GetRequiredService<IDailyProductionRepository>();
            var reportService = scope.ServiceProvider.GetRequiredService<DailyProductionReportService>();

            var records = await productionRepo.GetByDateAsync(EntryDate);

            // القطع التامة من DailyProductionReportService — **المكان الوحيد**
            // اللي بيعرف يفرّق بين "شغل اتعمل" و"منتج خرج". جمع السجلات
            // هنا كان بيحسب القطعة مرة لكل مرحلة عدّت عليها.
            var report = await reportService.GetAsync(EntryDate);
            DayTotalPieces = report.TotalCompletedPieces;
            DayStartedPieces = report.TotalStartedPieces;
            DayScrapPieces = report.TotalScrapPieces;

            // الباقي مقاييس شغل مش إنتاج، فمجموع السجلات صح فيها:
            // اليومية بتتحسب على اللي العامل عمله فعلاً على مرحلته
            DayTotalWorkdays = Math.Round(records.Sum(r => r.WorkdaysCompleted), 2);
            DayWorkersCount = records.Select(r => r.WorkerId).Distinct().Count();
            DayProductsCount = records.Select(r => r.ProductionStage.ProductId).Distinct().Count();
            _entryDayRecordCount = records.Count;

            // "اليوم فاضي" بقى معناه مفيش سجلات خالص — مش إن التام صفر.
            // يوم شغل كامل في نص الخط تامه صفر وهو مش فاضي.
            RefreshDaySummaryFlags();
        }

        /// <summary>
        /// تبويب "سجلات اليوم": نفس منطق تبويب "الإنتاج" (يوم/أسبوع/شهر
        /// حسب RecordsGrain)، وبعده فلتر منتج اختياري بيتبني من منتجات
        /// الفترة المعروضة بس. مستقل تمامًا عن ملخص EntryDate فوق.
        /// </summary>
        private async Task LoadRecordsTabAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var productionRepo = scope.ServiceProvider.GetRequiredService<IDailyProductionRepository>();

            IReadOnlyList<DailyProduction> records;

            switch (RecordsGrain)
            {
                case ChartGrain.Week:
                    var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(RecordsDate);
                    records = await productionRepo.GetByRangeAsync(weekStart, weekEnd);
                    RecordsPeriodLabel = $"الأسبوع من {weekStart:dd/MM} إلى {weekEnd:dd/MM}";
                    break;

                case ChartGrain.Month:
                    var monthStart = new DateTime(RecordsDate.Year, RecordsDate.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    records = await productionRepo.GetByRangeAsync(monthStart, monthEnd);
                    RecordsPeriodLabel = $"شهر {monthStart:MM/yyyy}";
                    break;

                default:
                    records = await productionRepo.GetByDateAsync(RecordsDate);
                    RecordsPeriodLabel = "";
                    break;
            }

            _allDayRecords.Clear();
            foreach (var r in records.OrderBy(r => r.Date).ThenBy(r => r.Worker.SortOrder).ThenBy(r => r.Id))
            {
                _allDayRecords.Add(new DayRecordRow
                {
                    RecordId = r.Id,
                    ProductId = r.ProductionStage.ProductId,
                    ProductName = r.ProductionStage.Product.Name,
                    WorkerName = r.Worker.FullName,
                    StageDisplay = $"{r.ProductionStage.Product.Name} / {r.ProductionStage.StageName}",
                    PieceCount = r.PieceCount,
                    QuotaAtEntry = r.PiecesPerWorkdayAtEntry,
                    Workdays = r.WorkdaysCompleted,
                    Date = r.Date
                });
            }

            // خيارات الفلتر: منتجات الفترة المعروضة بس — "الأسبوع/الشهر"
            // بيتغيّر ممكن يخلّي منتج مختار قبل كده مالوش سجلات دلوقتي،
            // فبيرجع لـ"كل المنتجات" تلقائي بدل ما يعرض قايمة فاضية بصمت
            var previousProductId = SelectedRecordsProduct?.ProductId;

            RecordsProductOptions.Clear();
            RecordsProductOptions.Add(new RecordsProductOption(null, "كل المنتجات"));
            foreach (var product in _allDayRecords
                         .Select(r => (r.ProductId, r.ProductName))
                         .Distinct()
                         .OrderBy(p => p.ProductName))
                RecordsProductOptions.Add(new RecordsProductOption(product.ProductId, product.ProductName));

            // نداء صريح دايمًا: RecordsProductOption record فبالمساواة بالقيمة —
            // لو نفس المنتج المختار قبل كده لسه موجود، الـ setter مش هيلاقي
            // فرق ومش هينادي ApplyRecordsProductFilter لوحده، مع إن
            // _allDayRecords اتغيّرت فعلاً (فترة تانية) ولازم DayRecords تتحدّث
            SelectedRecordsProduct = RecordsProductOptions.FirstOrDefault(o => o.ProductId == previousProductId)
                                      ?? RecordsProductOptions[0];
            ApplyRecordsProductFilter();
        }

        private void ApplyRecordsProductFilter()
        {
            var productId = SelectedRecordsProduct?.ProductId;

            DayRecords.Clear();
            foreach (var row in _allDayRecords.Where(r => productId is null || r.ProductId == productId))
                DayRecords.Add(row);

            OnPropertyChanged(nameof(RecordsTabIsEmpty));
        }

        /// <summary>مفيش سجلات في الفترة/الفلتر المختارين حاليًا — رسالة التبويب الفاضي</summary>
        public bool RecordsTabIsEmpty => DayRecords.Count == 0;

        [RelayCommand]
        private async Task EditDayRecordAsync(DayRecordRow? row)
        {
            if (row is null) return;

            var dialog = new Views.EditProductionDialog { Owner = Application.Current.MainWindow };
            dialog.LoadRecord(row.WorkerName, row.StageDisplay, row.PieceCount);
            if (dialog.ShowDialog() != true) return;

            // تصحيح القطع بيعيد حساب اليومية، واليومية هي الأجر — نفس
            // بوابة حذف السجل بالظبط
            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "تصحيح عدد القطع",
                $"{row.WorkerName} — {row.StageDisplay}\n" +
                $"من {row.PieceCount:N0} قطعة إلى {dialog.NewPieceCount:N0}.",
                SensitiveActionKind.Save,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var workdayService = scope.ServiceProvider.GetRequiredService<WorkdayCalculationService>();
                await workdayService.UpdateProductionAsync(row.RecordId, dialog.NewPieceCount, gate.Password);

                // إعادة تحميل كل حاجة مرتبطة باليوم — الأرقام بتتصحح في كل مكان فورًا
                await ReloadForDateAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في التصحيح");
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
                    SensitiveActionKind.Delete,
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var workdayService = scope.ServiceProvider.GetRequiredService<WorkdayCalculationService>();
                var result = await workdayService.DeleteProductionAsync(
                    row.RecordId, input.Password, input.Reason);

                if (!result.IsDeleted)
                {
                    Notify.Warn(result.Message, "مش هينفع");
                    return;
                }

                await ReloadForDateAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في الحذف");
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
                // شريحة نوع مش حالة حضور: بتعرض عمال الساعة بكل حالاتهم.
                // موجودة عشان الشيفتات ليها منطق مختلف، والقايمة الموحّدة
                // بتخلطهم مع عمال الإنتاج
                AttendanceFilter.Hourly => AttendanceRows.Where(r => r.IsHourly),
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
        public bool IsFilterHourly => ActiveAttendanceFilter == AttendanceFilter.Hourly;

        private void RefreshFilterFlags()
        {
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterPresent));
            OnPropertyChanged(nameof(IsFilterExcused));
            OnPropertyChanged(nameof(IsFilterUnexcused));
            OnPropertyChanged(nameof(IsFilterUnset));
            OnPropertyChanged(nameof(IsFilterHourly));
        }

        // ------- عدّادات الملخص اللي فوق القائمة -------

        public int PresentCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.Present);
        public int ExcusedCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.AbsentWithPermission);
        public int UnexcusedCount => AttendanceRows.Count(r => r.SelectedStatus == AttendanceStatus.AbsentWithoutPermission);
        public int UnsetCount => AttendanceRows.Count(r => r.SelectedStatus is null);
        public int HourlyCount => AttendanceRows.Count(r => r.IsHourly);
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
            OnPropertyChanged(nameof(HourlyCount));
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
            foreach (var w in workers.OrderBy(w => w.IsHourly).ThenBy(w => w.SortOrder))
            {
                savedStatuses.TryGetValue(w.Id, out var saved);
                var hasSaved = savedStatuses.ContainsKey(w.Id);
                var hasWork = workersWithWork.Contains(w.Id);

                var savedEndHour = w.IsHourly && hourlyLogs.TryGetValue(w.Id, out var logged)
                    ? logged
                    : (int?)null;

                var row = new AttendanceRow(
                    w.Id,
                    w.FullName,
                    w.IsHourly,
                    w.IsHourly ? w.HourlyRole!.Value.ToArabicName() : "بالقطعة")
                {
                    HasLoggedWork = hasWork,
                    SavedStatus = hasSaved ? saved : null,
                    SavedEndHour = savedEndHour
                };

                // المحفوظ بيكسب دايمًا؛ ولو مفيش محفوظ والعامل له شغل
                // مسجّل → "حاضر" تلقائي وظاهر قدام المستخدم قبل ما يحفظ
                var initialStatus = hasSaved
                    ? saved
                    : hasWork ? AttendanceStatus.Present : (AttendanceStatus?)null;

                row.SelectStatusSilently(initialStatus);

                if (savedEndHour is not null)
                    row.SelectShiftSilently(savedEndHour);

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
            // **اللي اتغيّر بس.** قبل كده كان بيتبعت كل صف عليه حالة —
            // يعني تعديل عامل واحد كان بيعيد كتابة الـ 13 عامل كلهم،
            // ويقول للمستخدم "تم حفظ حضور 13 عامل". ده كان بيخلي
            // المستخدم يفتكر إن البرنامج سجّل الباقي تاني، وبحق: إعادة
            // الكتابة كانت بتعيد حساب يوميات العمال بالساعة وتعيد
            // مصالحة جزاءات الغياب لناس مالهمش دعوة بالتعديل.
            //
            // الحالة المحفوظة موجودة في كل صف (SavedStatus/SavedEndHour)
            // فالمقارنة محلية من غير أي استعلام زيادة.
            var rowsToSave = AttendanceRows
                .Where(r => r.SelectedStatus is not null && r.HasUnsavedChange)
                .ToList();

            if (rowsToSave.Count == 0)
            {
                Notify.Info(
                    AttendanceRows.Any(r => r.SelectedStatus is not null)
                        ? "مفيش أي تعديل جديد يتحفظ — كل الحالات محفوظة زي ما هي"
                        : "مفيش أي حالة حضور محددة للحفظ",
                    "تنبيه");
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
                Notify.Warn("العمال دول ليهم شغل مسجّل النهارده ومتعلّم عليهم غياب:\n" + names +
                    "\n\nعامل شغل مينفعش يتسجل غايب في نفس اليوم. لو فعلاً كانوا غايبين، " +
                    "امسح شغلهم الأول من تبويب \"سجلات اليوم\".", "تعارض: شغل مسجّل مع غياب");
                return;
            }

            // كلمة سر واحدة للدفعة كلها. الحفظ ده بيولّد جزاءات غياب
            // بتنقص من الأجور، فهو عملية بتلمس فلوس — بس مرة في اليوم
            // مش مرة لكل عامل، عشان ميبقاش عبء يومي.
            // الصفوف بقت المتغيّرة بس، فالعدد ده جزاءات **جديدة** فعلاً
            // مش عدّ للجزاءات الموجودة أصلاً
            var unexcused = rowsToSave.Count(r => r.SelectedStatus == AttendanceStatus.AbsentWithoutPermission);
            var gateNote = unexcused > 0
                ? $"\n\nهيتسجل كمان {unexcused} جزاء غياب تلقائي (نص يومية لكل واحد)."
                : "";

            // بنسمّي العمال لما يكونوا قلايل: المستخدم لازم يشوف إن
            // اللي هيتحفظ هو اللي عدّله بالظبط، مش الورديّة كلها
            var whoChanged = rowsToSave.Count <= 5
                ? "\n" + string.Join("\n", rowsToSave.Select(r => $"  • {r.FullName}"))
                : "";

            var gateInput = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "حفظ تعديلات الحضور",
                $"هيتحفظ تعديل على {rowsToSave.Count} عامل ليوم {EntryDate:yyyy/MM/dd}."
                    + whoChanged + gateNote,
                SensitiveActionKind.Save,
                passwordRequired: true,
                // مفيش سبب مكتوب: ده حفظ يومي مش حذف
                reasonRequired: false);

            if (gateInput is null) return;

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
                    result = await attendanceService.RecordAttendanceBatchAsync(
                        EntryDate, entries, gateInput.Password);
                }

                var penaltyLines = "";
                if (result.AutoPenaltiesCreated > 0)
                    penaltyLines += $"\n⚠ اتسجل {result.AutoPenaltiesCreated} جزاء غياب تلقائي (نص يومية لكل واحد)";
                if (result.AutoPenaltiesRemoved > 0)
                    penaltyLines += $"\n✔ اتشال {result.AutoPenaltiesRemoved} جزاء غياب تلقائي (الحالة اتغيّرت)";

                Notify.Info(
                    $"تم حفظ تعديل حضور {result.SavedCount} عامل بتاريخ {EntryDate:yyyy/MM/dd}{penaltyLines}",
                    "تم الحفظ");

                await LoadAttendanceAsync();
                await LoadPenaltiesAsync(); // الجزاءات التلقائية تظهر/تختفي فورًا
            }
            catch (InvalidOperationException ex)
            {
                // قاعدة الحماية: غياب لعامل له شغل في نفس اليوم بيترفض برسالة بأسماء العمال
                Notify.Warn(ex.Message, "تعارض في البيانات");
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
                Notify.Info("اختار العامل الأول", "تنبيه");
                return;
            }
            if (string.IsNullOrWhiteSpace(PenaltyReason))
            {
                Notify.Info("اكتب سبب الجزاء", "تنبيه");
                return;
            }
            if (SelectedDeduction is null) return;

            // الجزاء بيخصم من أجر عامل حقيقي — عملية بتلمس فلوس
            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "تسجيل جزاء",
                $"جزاء \"{PenaltyReason}\" على {PenaltyWorker.FullName} " +
                $"بخصم {SelectedDeduction.Display} يوم {EntryDate:yyyy/MM/dd}.",
                SensitiveActionKind.Save,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();
                await penaltyService.RecordPenaltyAsync(
                    PenaltyWorker.WorkerId, EntryDate, PenaltyReason, SelectedDeduction.Value,
                    operationsPassword: gate.Password);
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return;
            }

            // تفريغ الفورم وإعادة تحميل قائمة اليوم
            PenaltyReason = string.Empty;
            PenaltyWorker = null;
            await LoadPenaltiesAsync();
        }

        /// <summary>
        /// يعدّل جزاء يدوي متسجّل. الجزاءات التلقائية بترفض التعديل من
        /// الخدمة نفسها — الشاشة بتعرض السبب.
        /// </summary>
        [RelayCommand]
        private async Task EditPenaltyAsync(PenaltyRow? row)
        {
            if (row is null) return;

            var dialog = new PenaltyEditDialog(row.WorkerName, row.Reason, row.DeductionName)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                "تعديل جزاء",
                $"تعديل جزاء {row.WorkerName} ليوم {EntryDate:yyyy/MM/dd}.",
                SensitiveActionKind.Save,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PenaltyService>()
                    .UpdatePenaltyAsync(
                        row.PenaltyId, dialog.PenaltyReason, dialog.Deduction,
                        operationsPassword: gate.Password);

                await LoadPenaltiesAsync();
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
        }

        [RelayCommand]
        private async Task RemovePenaltyAsync(PenaltyRow? row)
        {
            if (row is null) return;

            if (!Notify.Ask($"حذف جزاء \"{row.Reason}\" عن {row.WorkerName}؟", "تأكيد"))
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();
                await penaltyService.RemovePenaltyAsync(row.PenaltyId);
                await LoadPenaltiesAsync();
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
            }
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
                    TypeColor = a.Type == WageAdjustmentType.Bonus ? "GoodBrush" : "DangerBrush",
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
                Notify.Info("اختار العامل الأول", "تنبيه");
                return;
            }
            if (SelectedAdjustmentType is null)
            {
                Notify.Info("اختار النوع (سلفة/حافز)", "تنبيه");
                return;
            }
            if (!decimal.TryParse(AdjustmentAmount, out var amount) || amount <= 0)
            {
                Notify.Info("اكتب مبلغ صحيح أكبر من صفر", "تنبيه");
                return;
            }

            // فلوس بتتضاف أو تتخصم من الأجر مباشرة — بوابة زي الجزاءات
            var typeName = SelectedAdjustmentType.Value == WageAdjustmentType.Bonus ? "حافز" : "سلفة";
            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                $"تسجيل {typeName}",
                $"{typeName} بمبلغ {amount:N0} ج على {AdjustmentWorker.FullName} بتاريخ {EntryDate:yyyy/MM/dd}.",
                SensitiveActionKind.Save,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<WageAdjustmentService>();
                await service.RecordAdjustmentAsync(
                    AdjustmentWorker.WorkerId, EntryDate, SelectedAdjustmentType.Value, amount,
                    AdjustmentNote, gate.Password);
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return;
            }

            // تفريغ الفورم وإعادة تحميل قائمة اليوم
            AdjustmentAmount = string.Empty;
            AdjustmentNote = string.Empty;
            AdjustmentWorker = null;
            await LoadAdjustmentsAsync();
        }

        // ======================= قسم الهالك =======================

        /// <summary>
        /// هالك اليوم: القطع اللي اتشالت من الخط ومش هتتكمّل.
        ///
        /// البرنامج بيسأل لوحده عن الفرق بين المراحل بعد حفظ الرحلة،
        /// فالتبويب ده أساسًا للحالة اللي مفيش فيها فرق يظهر — قطعة
        /// خلصت **آخر مرحلة** والجودة رفضتها.
        /// </summary>
        public ObservableCollection<ScrapRecordDto> DayScrap { get; } = new();

        public string ScrapTotalText =>
            DayScrap.Count == 0 ? "" : $"{DayScrap.Sum(s => s.PieceCount):N0} قطعة";

        private async Task LoadScrapAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var records = await scope.ServiceProvider
                .GetRequiredService<ScrapService>()
                .GetByDateAsync(EntryDate);

            DayScrap.Clear();
            foreach (var record in records) DayScrap.Add(record);

            OnPropertyChanged(nameof(ScrapTotalText));
        }

        [RelayCommand]
        private async Task AddScrapAsync()
        {
            List<ScrapProductChoice> products;
            List<ScrapReason> reasons;

            using (var scope = _scopeFactory.CreateScope())
            {
                var allProducts = await scope.ServiceProvider
                    .GetRequiredService<IProductRepository>()
                    .GetAllWithStagesAsync();

                // منتج الأول، وبعدين مراحله بس — الهالك ممكن يتسجّل على أي
                // مرحلة، وآخر مرحلة معلّمة عشان المستخدم يعرف إن دي اللي
                // بتخصم من الإنتاج التام
                products = allProducts
                    .OrderBy(p => p.Name)
                    .Select(p =>
                    {
                        var line = ProductionLine.Active(p);
                        var stages = line.Select((stage, index) => new ScrapStageChoice(
                            stage.Id,
                            index == line.Count - 1 ? $"{stage.StageName} (آخر مرحلة)" : stage.StageName,
                            0)).ToList();

                        return new ScrapProductChoice(p.Id, p.Name, stages);
                    })
                    .Where(p => p.Stages.Count > 0)
                    .ToList();

                reasons = await scope.ServiceProvider
                    .GetRequiredService<ScrapService>()
                    .GetActiveReasonsAsync();
            }

            if (products.Count == 0)
            {
                Notify.Info("مفيش مراحل إنتاج متسجلة — ضيف منتج ومراحله الأول", "تنبيه");
                return;
            }

            var dialog = ScrapDialog.ForStage(Application.Current.MainWindow, products, reasons);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ScrapService>().RecordAsync(
                    dialog.StageId, EntryDate, dialog.PieceCount,
                    dialog.ReasonId, dialog.Note,
                    scope.ServiceProvider.GetRequiredService<CurrentUserContext>().ActorName);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return;
            }

            await LoadScrapAsync();
            await LoadDaySummaryAsync(); // ملخص اليوم بيتغيّر مع الهالك
        }

        [RelayCommand]
        private async Task RemoveScrapAsync(ScrapRecordDto? row)
        {
            if (row is null) return;

            if (!Notify.Ask(
                    $"حذف {row.PieceCount:N0} قطعة هالك على \"{row.StageDisplay}\"؟\n" +
                    "القطع هترجع تتحسب في الشغل الواقف أو الإنتاج التام.", "تأكيد"))
                return;

            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ScrapService>().RemoveAsync(row.Id);

            await LoadScrapAsync();
            await LoadDaySummaryAsync();
        }

        [RelayCommand]
        private async Task RemoveAdjustmentAsync(AdjustmentRow? row)
        {
            if (row is null) return;

            // حذف سلفة بيرجّع فلوس لأجر العامل زي ما تسجيلها بتخصمها
            var gate = SensitiveActionDialog.Ask(
                Application.Current.MainWindow,
                $"حذف {row.TypeName}",
                $"{row.TypeName} ({row.AmountText}) عن {row.WorkerName}.",
                SensitiveActionKind.Delete,
                passwordRequired: true,
                reasonRequired: false);

            if (gate is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<WageAdjustmentService>();
                await service.RemoveAdjustmentAsync(row.AdjustmentId, gate.Password);
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message, "مش هينفع");
                return;
            }

            await LoadAdjustmentsAsync();
        }
    }
}
