using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
        private List<(int ProductId, string ProductName, int? FamilyId, string? FamilyName)> _catalog = new();
        private int _nextCustomPeriodNumber = 1;

        public PlanCreationWizardViewModel(IServiceScopeFactory scopeFactory, int year, int month)
        {
            _scopeFactory = scopeFactory;
            _year = year;
            _month = month;
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

        /// <summary>true لو حصل حفظ ناجح فعلاً — الشاشة اللي فتحت الـwizard بتعيد التحميل بناءً عليه</summary>
        public bool Saved { get; private set; }

        private async Task<List<(int ProductId, string ProductName, int? FamilyId, string? FamilyName)>> LoadCatalogAsync()
        {
            if (_catalog.Count > 0) return _catalog;

            using var scope = _scopeFactory.CreateScope();
            var products = await scope.ServiceProvider.GetRequiredService<MonthlyPlanService>()
                .GetForMonthAsync(_year, _month); // subPeriodId=0 بس لجلب كتالوج المنتجات — الكمية بتتجاهل هنا

            _catalog = products.Select(p => (p.ProductId, p.ProductName, p.FamilyId, p.FamilyName)).ToList();
            return _catalog;
        }

        private static WizardPeriodRow BuildPeriodRow(
            List<(int ProductId, string ProductName, int? FamilyId, string? FamilyName)> catalog,
            int subPeriodId, string label)
        {
            var familyGroups = new List<WizardFamilyGroupRow>();

            foreach (var g in catalog.Where(p => p.FamilyId is not null)
                         .GroupBy(p => (p.FamilyId!.Value, p.FamilyName ?? ""))
                         .OrderBy(g => g.Key.Item2))
            {
                var rows = g.Select(p => new WizardProductRow { ProductId = p.ProductId, ProductName = p.ProductName }).ToList();
                familyGroups.Add(new WizardFamilyGroupRow($"{g.Key.Item2} ({g.Count()})", rows));
            }

            var noFamily = catalog.Where(p => p.FamilyId is null).ToList();
            if (noFamily.Count > 0)
            {
                var rows = noFamily.Select(p => new WizardProductRow { ProductId = p.ProductId, ProductName = p.ProductName }).ToList();
                familyGroups.Add(new WizardFamilyGroupRow($"بدون عيلة ({noFamily.Count})", rows));
            }

            return new WizardPeriodRow(familyGroups) { SubPeriodId = subPeriodId, Label = label };
        }

        [RelayCommand]
        private async Task UseWholeMonthAsync()
        {
            var catalog = await LoadCatalogAsync();
            Periods.Clear();
            Periods.Add(BuildPeriodRow(catalog, subPeriodId: 0, label: "الشهر كامل"));
            CurrentPeriodIndex = 0;
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
                Periods.Add(BuildPeriodRow(catalog, subPeriod.Id, subPeriod.Label));

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
