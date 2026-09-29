using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    public enum WizardStep { Duration, Entry, Saving }

    /// <summary>
    /// عقل wizard "إضافة خطة جديدة للشهر" — مسار افتراضي (الشهر كامل)
    /// ضغطة واحدة، ومسار متقدم (فترات فرعية مرقّمة) اختياري. كل منتجات
    /// كل الفترات بتيجي من نفس كتالوج المنتجات (استعلام واحد)، كل فترة
    /// بس بتحمل نسخة QuantityText مستقلة لكل منتج. شوف CLAUDE.md.
    /// </summary>
    public partial class PlanCreationWizardViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _year;
        private readonly int _month;
        private List<MonthlyPlanProductDto> _catalog = new();
        private int _nextCustomPeriodNumber = 1;

        /// <summary>true = فتحنا الـwizard على خطة موجودة بالفعل (زرار "تعديل الخطة") — شوف LoadExistingPlanAsync</summary>
        public bool IsEditMode { get; }

        public PlanCreationWizardViewModel(IServiceScopeFactory scopeFactory, int year, int month, bool editMode = false)
        {
            _scopeFactory = scopeFactory;
            _year = year;
            _month = month;
            IsEditMode = editMode;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDurationStep))]
        [NotifyPropertyChangedFor(nameof(IsEntryStep))]
        [NotifyPropertyChangedFor(nameof(IsSavingStep))]
        private WizardStep _currentStep = WizardStep.Duration;

        public bool IsDurationStep => CurrentStep == WizardStep.Duration;
        public bool IsEntryStep => CurrentStep == WizardStep.Entry;
        public bool IsSavingStep => CurrentStep == WizardStep.Saving;

        [ObservableProperty] private bool _isCustomizing;
        [ObservableProperty] private DateTime? _customStart;
        [ObservableProperty] private DateTime? _customEnd;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDurationError))]
        private string _durationError = "";

        public bool HasDurationError => DurationError.Length > 0;

        public ObservableCollection<WizardPeriodRow> Periods { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentPeriod))]
        [NotifyPropertyChangedFor(nameof(IsLastPeriod))]
        private int _currentPeriodIndex;

        public WizardPeriodRow? CurrentPeriod => CurrentPeriodIndex < Periods.Count ? Periods[CurrentPeriodIndex] : null;
        public bool HasMultiplePeriods => Periods.Count > 1;
        public bool IsLastPeriod => CurrentPeriodIndex >= Periods.Count - 1;

        /// <summary>
        /// [ObservableProperty] بيتجاهل الـnotify لو القيمة الجديدة زي القديمة —
        /// CurrentPeriodIndex غالبًا بيتحط 0 وهو أصلاً 0 (القيمة الافتراضية)
        /// بعد ما Periods اتملت لسه، فـCurrentPeriod/IsLastPeriod مكانوش
        /// بيتحدّثوا خالص وخطوة الإدخال كانت بتطلع فاضية بالكامل. نداء صريح
        /// هنا بيضمن التحديث حتى لو الـindex نفسه ما اتغيرش.
        /// </summary>
        private void RefreshCurrentPeriod()
        {
            OnPropertyChanged(nameof(CurrentPeriod));
            OnPropertyChanged(nameof(IsLastPeriod));
        }

        /// <summary>true لو حصل حفظ ناجح فعلاً — الشاشة اللي فتحت الـwizard بتعيد التحميل بناءً عليه</summary>
        public bool Saved { get; private set; }

        private async Task<List<MonthlyPlanProductDto>> LoadCatalogAsync()
        {
            if (_catalog.Count > 0) return _catalog;

            using var scope = _scopeFactory.CreateScope();
            // subPeriodId=0 بس لجلب كتالوج المنتجات (أسماء/عائلات) — الكمية بتتجاهل هنا،
            // مسار الخطة الجديدة دايمًا بيبدأ فاضي (prefillFromExisting: false تحت)
            _catalog = await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                .GetForMonthAsync(_year, _month);
            return _catalog;
        }

        /// <summary>
        /// <paramref name="prefillFromExisting"/>: true (وضع التعديل) يملأ QuantityText من
        /// PlannedQuantity الموجودة بالفعل (0 = لسه مفيش قيمة، يفضل فاضي)، false (خطة جديدة) يبدأ فاضي دايمًا.
        /// </summary>
        private static WizardPeriodRow BuildPeriodRow(
            List<MonthlyPlanProductDto> products, int subPeriodId, string label, bool prefillFromExisting)
        {
            WizardProductRow ToRow(MonthlyPlanProductDto p)
            {
                var row = new WizardProductRow { ProductId = p.ProductId, ProductName = p.ProductName };
                if (prefillFromExisting && p.PlannedQuantity > 0) row.QuantityText = p.PlannedQuantity.ToString();
                return row;
            }

            var familyGroups = new List<WizardFamilyGroupRow>();

            foreach (var g in products.Where(p => p.FamilyId is not null)
                         .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                         .OrderBy(g => g.Key.Item2))
                familyGroups.Add(new WizardFamilyGroupRow($"{g.Key.Item2} ({g.Count()})", g.Select(ToRow).ToList()));

            var noFamily = products.Where(p => p.FamilyId is null).ToList();
            if (noFamily.Count > 0)
                familyGroups.Add(new WizardFamilyGroupRow($"بدون عيلة ({noFamily.Count})", noFamily.Select(ToRow).ToList()));

            return new WizardPeriodRow(familyGroups) { SubPeriodId = subPeriodId, Label = label };
        }

        /// <summary>
        /// وضع التعديل: بيحمّل الفترات الموجودة فعلاً (أو الشهر كامل لو مفيش فترات فرعية) بقيمها
        /// الحالية، ويقفز لخطوة الإدخال على طول. مش [RelayCommand]: بينادى من كود-behind النافذة
        /// (Loaded) مش من زرار في الـXAML.
        /// </summary>
        public async Task LoadExistingPlanAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<MonthlyPlanService>();
            var subPeriods = await service.GetSubPeriodsForMonthAsync(_year, _month);

            Periods.Clear();
            if (subPeriods.Count == 0)
            {
                var products = await service.GetForMonthAsync(_year, _month);
                Periods.Add(BuildPeriodRow(products, subPeriodId: 0, label: "الشهر كامل", prefillFromExisting: true));
            }
            else
            {
                foreach (var sp in subPeriods)
                {
                    var products = await service.GetForMonthAsync(_year, _month, sp.Id);
                    Periods.Add(BuildPeriodRow(products, sp.Id, sp.Label, prefillFromExisting: true));
                }
            }

            CurrentPeriodIndex = 0;
            RefreshCurrentPeriod();
            CurrentStep = WizardStep.Entry;
        }

        [RelayCommand]
        private async Task UseWholeMonthAsync()
        {
            var catalog = await LoadCatalogAsync();
            Periods.Clear();
            Periods.Add(BuildPeriodRow(catalog, subPeriodId: 0, label: "الشهر كامل", prefillFromExisting: false));
            CurrentPeriodIndex = 0;
            RefreshCurrentPeriod();
            CurrentStep = WizardStep.Entry;
        }

        [RelayCommand]
        private void ToggleCustomizing() => IsCustomizing = !IsCustomizing;

        [RelayCommand]
        private async Task AddCustomPeriodAsync()
        {
            DurationError = "";
            if (CustomStart is not { } start || CustomEnd is not { } end)
            {
                DurationError = "لازم تختار تاريخ البداية والنهاية";
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var subPeriod = await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                    .CreateSubPeriodAsync($"خطة {_nextCustomPeriodNumber}", start, end);
                _nextCustomPeriodNumber++;

                var catalog = await LoadCatalogAsync();
                Periods.Add(BuildPeriodRow(catalog, subPeriod.Id, subPeriod.Label, prefillFromExisting: false));

                CustomStart = null;
                CustomEnd = null;
            }
            catch (ArgumentException ex)
            {
                DurationError = ex.Message;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartEntry))]
        private void StartEntry()
        {
            CurrentPeriodIndex = 0;
            RefreshCurrentPeriod();
            CurrentStep = WizardStep.Entry;
        }

        private bool CanStartEntry() => Periods.Count > 0;

        [RelayCommand(CanExecute = nameof(CanGoNextPeriod))]
        private void NextPeriod() => CurrentPeriodIndex++;

        private bool CanGoNextPeriod() => !IsLastPeriod;

        [RelayCommand]
        private void BackToDuration()
        {
            Periods.Clear();
            CurrentPeriodIndex = 0;
            CurrentStep = WizardStep.Duration;
        }

        /// <summary>
        /// true لو تم الحفظ فعلاً (بعد تأكيد المستخدم لو في منتجات ناقصة، أو
        /// من غير تأكيد لو كله متسجل) — false لو المستخدم رجع من غير حفظ.
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            var totalCount = Periods.Sum(p => p.TotalCount);
            var enteredCount = Periods.Sum(p => p.EnteredCount);

            if (enteredCount < totalCount)
            {
                var missing = totalCount - enteredCount;
                var confirmed = Notify.Ask(
                    $"لسه في {missing} منتج من {totalCount} ما اتسجلش رقم له. تقدر تكمل بعدين — تحفظ اللي اتسجل دلوقتي؟",
                    "منتجات ناقصة");
                if (!confirmed) return false;
            }

            CurrentStep = WizardStep.Saving;

            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<MonthlyPlanService>();
                foreach (var period in Periods)
                foreach (var family in period.FamilyGroups)
                foreach (var product in family.Products.Where(p => p.IsEntered))
                    await service.SetPlanAsync(product.ProductId, _year, _month, product.Quantity, period.SubPeriodId);
            }

            Saved = true;
            return true;
        }
    }
}
