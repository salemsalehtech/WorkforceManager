using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة المنتجات والمراحل: قائمة المنتجات (مع بحث وإظهار
    /// الموقوف)، ولوحة تفاصيل بتعرض مراحل المنتج المحدد بيومياتها،
    /// مع كل عمليات الإدارة: إضافة/تعديل/إيقاف منتج أو مرحلة.
    /// تعديل اليومية بيسري على التسجيلات الجديدة فقط — القديم محمي
    /// بالـ Snapshot، والرسائل في الشاشة بتوضح ده للمستخدم.
    /// </summary>
    public partial class ProductsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ProductsViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // ------- حالة الشاشة -------

        [ObservableProperty]
        private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        /// <summary>
        /// الفلتر السريع المختار — "الكل" افتراضيًا عشان أول ما الشاشة
        /// تفتح تعرض كل المنتجات، والمستخدم يختار "شغّالين" بنفسه لو
        /// عايز يضيّق على فترة معيّنة.
        /// </summary>
        [ObservableProperty]
        private ProductFilter _activeFilter = ProductFilter.All;

        partial void OnActiveFilterChanged(ProductFilter value)
        {
            OnPropertyChanged(nameof(IsFilterWorked));
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterInactive));
            ApplyFilter();
        }

        public bool IsFilterWorked => ActiveFilter == ProductFilter.WorkedThisPeriod;
        public bool IsFilterAll => ActiveFilter == ProductFilter.All;
        public bool IsFilterInactive => ActiveFilter == ProductFilter.Inactive;

        [RelayCommand]
        private void SetFilter(string? filter) =>
            ActiveFilter = filter switch
            {
                "all" => ProductFilter.All,
                "inactive" => ProductFilter.Inactive,
                _ => ProductFilter.WorkedThisPeriod
            };

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        // ------- الفترة اللي الإحصائيات والفلترة بتتحسب عليها -------

        /// <summary>
        /// الفترة الافتراضية = أسبوع الشغل الحالي (الخميس → الأربع)، نفس
        /// تعريف باقي التطبيق. المستخدم يقدر يوسّعها من الشاشة.
        /// </summary>
        [ObservableProperty]
        private DateTime _periodFrom = ProductActivityService.CurrentWeek(DateTime.Today).From;

        [ObservableProperty]
        private DateTime _periodTo = ProductActivityService.CurrentWeek(DateTime.Today).To;

        partial void OnPeriodFromChanged(DateTime value) => SafeAsync.Run(LoadAsync);
        partial void OnPeriodToChanged(DateTime value) => SafeAsync.Run(LoadAsync);

        /// <summary>قائمة المدة مفتوحة — الاختيار بيقفلها بنفسه</summary>
        [ObservableProperty]
        private bool _isPeriodMenuOpen;

        /// <summary>يرجّع الفترة لأسبوع الشغل الحالي</summary>
        [RelayCommand]
        private void UseCurrentWeek()
        {
            var (from, to) = ProductActivityService.CurrentWeek(DateTime.Today);
            PeriodFrom = from;
            PeriodTo = to;
            IsPeriodMenuOpen = false;
        }

        /// <summary>الشهر الجاري: من أول يوم فيه لحد النهارده</summary>
        [RelayCommand]
        private void UseThisMonth()
        {
            var today = DateTime.Today;
            PeriodFrom = new DateTime(today.Year, today.Month, 1);
            PeriodTo = today;
            IsPeriodMenuOpen = false;
        }

        /// <summary>يوسّع الفترة لآخر 30 يوم</summary>
        [RelayCommand]
        private void UseLastMonth()
        {
            PeriodFrom = DateTime.Today.AddDays(-30);
            PeriodTo = DateTime.Today;
            IsPeriodMenuOpen = false;
        }

        /// <summary>وصف الفترة الكامل — بيتعرض جوّه قائمة المدة مش على الشاشة</summary>
        public string PeriodText => IsCurrentWeek
            ? $"أسبوع الشغل الحالي (الخميس {PeriodFrom:dd/MM} → الأربع {PeriodTo:dd/MM})"
            : $"من {PeriodFrom:yyyy/MM/dd} إلى {PeriodTo:yyyy/MM/dd}";

        /// <summary>
        /// اسم الفترة على زرارها — كلمتين مش تاريخين.
        ///
        /// التاريخ الكامل عايش جوّه القائمة، فالزرار بيقول "إيه المدة"
        /// والقائمة بتقول "من امتى لامتى" — كل معلومة في مكان واحد.
        /// </summary>
        public string PeriodLabel
        {
            get
            {
                if (IsCurrentWeek) return "الأسبوع ده";
                if (IsThisMonth) return "الشهر ده";
                if (IsLast30Days) return "آخر 30 يوم";
                return $"{PeriodFrom:dd/MM} → {PeriodTo:dd/MM}";
            }
        }

        public bool IsCurrentWeek
        {
            get
            {
                var (from, to) = ProductActivityService.CurrentWeek(DateTime.Today);
                return PeriodFrom.Date == from.Date && PeriodTo.Date == to.Date;
            }
        }

        public bool IsThisMonth
        {
            get
            {
                var today = DateTime.Today;
                return PeriodFrom.Date == new DateTime(today.Year, today.Month, 1) &&
                       PeriodTo.Date == today;
            }
        }

        public bool IsLast30Days =>
            PeriodFrom.Date == DateTime.Today.AddDays(-30) && PeriodTo.Date == DateTime.Today;

        // ------- عدّادات الملخص -------

        public int TotalProducts => _allProducts.Count;

        /// <summary>منتجات اتسجل عليها شغل فعلي في الفترة</summary>
        public int WorkedProductsCount => _allProducts.Count(p => p.WorkedInPeriod);

        /// <summary>منتجات فيها مشكلة تمنع الإنتاج عليها فعليًا</summary>
        public int NeedsAttentionCount => _allProducts.Count(p => p.IsActive && p.NeedsAttention);

        /// <summary>أكتر منتج إنتاجًا في الفترة (null = مفيش شغل خالص)</summary>
        public ProductRow? MostActiveProduct =>
            _allProducts.Where(p => p.WorkedInPeriod)
                .OrderByDescending(p => p.PiecesInPeriod).ThenBy(p => p.Name)
                .FirstOrDefault();

        /// <summary>
        /// أقل منتج إنتاجًا **من اللي اشتغلوا فعلًا**.
        ///
        /// المنتجات اللي مفيش عليها شغل خالص مستبعدة عن قصد: كلهم بصفر،
        /// فالنتيجة كانت هتبقى أول واحد أبجديًا — رقم مالوش أي معنى.
        /// </summary>
        public ProductRow? LeastActiveProduct =>
            _allProducts.Where(p => p.WorkedInPeriod)
                .OrderBy(p => p.PiecesInPeriod).ThenBy(p => p.Name)
                .FirstOrDefault();

        public string MostActiveName => MostActiveProduct?.Name ?? "—";
        public string MostActivePieces => MostActiveProduct is null
            ? "مفيش شغل مسجّل" : $"{MostActiveProduct.PiecesInPeriod:N0} قطعة";

        public string LeastActiveName => LeastActiveProduct?.Name ?? "—";
        public string LeastActivePieces => LeastActiveProduct is null
            ? "مفيش شغل مسجّل" : $"{LeastActiveProduct.PiecesInPeriod:N0} قطعة";

        /// <summary>منتج واحد بس اشتغل — "الأكتر" و"الأقل" هيبقوا هو</summary>
        public bool HasSingleWorkedProduct => WorkedProductsCount == 1;

        public string ResultsText => Products.Count == TotalProducts
            ? $"{Products.Count} منتج"
            : $"{Products.Count} من {TotalProducts}";

        public bool NoResults => Products.Count == 0 && _allProducts.Count > 0;

        private void RefreshSummary()
        {
            OnPropertyChanged(nameof(TotalProducts));
            OnPropertyChanged(nameof(WorkedProductsCount));
            OnPropertyChanged(nameof(NeedsAttentionCount));
            OnPropertyChanged(nameof(MostActiveProduct));
            OnPropertyChanged(nameof(LeastActiveProduct));
            OnPropertyChanged(nameof(MostActiveName));
            OnPropertyChanged(nameof(MostActivePieces));
            OnPropertyChanged(nameof(LeastActiveName));
            OnPropertyChanged(nameof(LeastActivePieces));
            OnPropertyChanged(nameof(HasSingleWorkedProduct));
            OnPropertyChanged(nameof(PeriodText));
            OnPropertyChanged(nameof(PeriodLabel));
            OnPropertyChanged(nameof(IsCurrentWeek));
            OnPropertyChanged(nameof(IsThisMonth));
            OnPropertyChanged(nameof(IsLast30Days));
            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
        }

        // ------- الفلاتر المركّبة (بتتجمع مع الشريحة بـ AND) -------

        /// <summary>فلترة بالمنتجات اللي فيها المرحلة دي</summary>
        [ObservableProperty]
        private StageNameFilterOption? _selectedStageFilter;

        partial void OnSelectedStageFilterChanged(StageNameFilterOption? value) => ApplyFilter();

        /// <summary>فلترة بالمنتجات اللي العامل ده اشتغل عليها في الفترة</summary>
        [ObservableProperty]
        private WorkerNameFilterOption? _selectedWorkerFilter;

        partial void OnSelectedWorkerFilterChanged(WorkerNameFilterOption? value) => ApplyFilter();

        /// <summary>ترتيب/تصفية بحجم الإنتاج في الفترة</summary>
        [ObservableProperty]
        private VolumeFilterOption? _selectedVolumeFilter;

        partial void OnSelectedVolumeFilterChanged(VolumeFilterOption? value) => ApplyFilter();

        public ObservableCollection<StageNameFilterOption> StageFilterOptions { get; } = new();
        public ObservableCollection<WorkerNameFilterOption> WorkerFilterOptions { get; } = new();

        public List<VolumeFilterOption> VolumeFilterOptions { get; } = new()
        {
            new(ProductVolumeSort.None, "الترتيب الافتراضي"),
            new(ProductVolumeSort.HighestFirst, "الأعلى إنتاجًا الأول"),
            new(ProductVolumeSort.LowestFirst, "الأقل إنتاجًا الأول")
        };

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
            (SelectedStageFilter?.StageName is not null ? 1 : 0) +
            (SelectedWorkerFilter?.WorkerId is not null ? 1 : 0) +
            ((SelectedVolumeFilter?.Sort ?? ProductVolumeSort.None) != ProductVolumeSort.None ? 1 : 0);

        public bool HasExtraFilters => ActiveFilterCount > 0;

        [RelayCommand]
        private void ClearExtraFilters()
        {
            SelectedStageFilter = StageFilterOptions.FirstOrDefault();
            SelectedWorkerFilter = WorkerFilterOptions.FirstOrDefault();
            SelectedVolumeFilter = VolumeFilterOptions[0];
        }

        /// <summary>يعرض المنتجات اللي فيها مشكلة بس</summary>
        [RelayCommand]
        private void ShowNeedsAttention()
        {
            SearchText = string.Empty;
            ActiveFilter = ProductFilter.All;
            ClearExtraFilters();

            var selectedId = SelectedProduct?.ProductId;
            Products.Clear();
            foreach (var p in _allProducts.Where(p => p.IsActive && p.NeedsAttention))
                Products.Add(p);

            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selectedId) ?? Products.FirstOrDefault();
            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
        }

        /// <summary>كل المنتجات المحمّلة من القاعدة (المصدر قبل الفلترة)</summary>
        private List<ProductRow> _allProducts = new();

        /// <summary>المنتجات المعروضة بعد البحث/الفلترة</summary>
        public ObservableCollection<ProductRow> Products { get; } = new();

        [ObservableProperty]
        private ProductRow? _selectedProduct;

        partial void OnSelectedProductChanged(ProductRow? value)
        {
            // تحديث لوحة المراحل فورًا عند تغيير المنتج المحدد
            Stages.Clear();
            if (value is null) return;
            foreach (var s in value.Stages.OrderBy(s => s.SortOrder))
                Stages.Add(s);
        }

        /// <summary>مراحل المنتج المحدد (مرتبة بترتيب خط الإنتاج)</summary>
        public ObservableCollection<StageRow> Stages { get; } = new();

        // ------- التحميل والفلترة -------

        /// <summary>
        /// يملا قوايم المراحل والعمال المتاحة للفلترة.
        ///
        /// المراحل بالاسم مش بالـ Id: نفس اسم المرحلة ("لمعة") بيتكرر عبر
        /// منتجات كتير بأرقام مختلفة، والمستخدم بيسأل "أنهي منتجات فيها
        /// لمعة؟" — مش "المرحلة رقم 42".
        /// </summary>
        private async Task LoadFilterOptionsAsync(
            IWorkerRepository workerRepo, IReadOnlyList<Product> products)
        {
            var previousStage = SelectedStageFilter?.StageName;
            var previousWorkerId = SelectedWorkerFilter?.WorkerId;

            StageFilterOptions.Clear();
            StageFilterOptions.Add(new StageNameFilterOption(null, "كل المراحل"));

            foreach (var stageName in products
                         .SelectMany(p => p.Stages.Where(s => s.IsActive))
                         .Select(s => s.StageName)
                         .Distinct()
                         .OrderBy(name => name))
                StageFilterOptions.Add(new StageNameFilterOption(stageName, stageName));

            WorkerFilterOptions.Clear();
            WorkerFilterOptions.Add(new WorkerNameFilterOption(null, "كل العمال"));

            foreach (var worker in (await workerRepo.GetAllAsync())
                         .Where(w => w.IsActive)
                         .OrderBy(w => w.SortOrder))
                WorkerFilterOptions.Add(new WorkerNameFilterOption(worker.Id, worker.FullName));

            SelectedStageFilter = StageFilterOptions.FirstOrDefault(o => o.StageName == previousStage)
                                  ?? StageFilterOptions[0];
            SelectedWorkerFilter = WorkerFilterOptions.FirstOrDefault(o => o.WorkerId == previousWorkerId)
                                   ?? WorkerFilterOptions[0];
            SelectedVolumeFilter ??= VolumeFilterOptions[0];
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var skillRepo = scope.ServiceProvider.GetRequiredService<IGenericRepository<WorkerSkill>>();
            var activityService = scope.ServiceProvider.GetRequiredService<ProductActivityService>();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();

            // عدد العمال المؤهلين لكل مرحلة — استعلام واحد وتجميع في الذاكرة.
            // ده اللي بيكشف المرحلة اللي "مفيش حد يعرف يعملها"، وهي مشكلة
            // بتوقف الإنتاج فعليًا لأن رحلة الإنتاج بتعرض المؤهلين بس.
            var qualifiedCountByStage = (await skillRepo.GetAllAsync())
                .GroupBy(ws => ws.ProductionStageId)
                .ToDictionary(g => g.Key, g => g.Count());

            // النشاط الفعلي في الفترة — منه بييجي "شغّال ولا لأ" والإحصائيات
            var activity = (await activityService.GetAsync(PeriodFrom, PeriodTo))
                .ToDictionary(a => a.ProductId);

            var products = await productRepo.GetAllWithStagesAsync();

            await LoadFilterOptionsAsync(workerRepo, products);

            _allProducts = products.Select(p => new ProductRow
            {
                ProductId = p.Id,
                Name = p.Name,
                Description = p.Description ?? "",
                IsActive = p.IsActive,
                ImageData = p.ImageData,
                RackingWorkerId = p.RackingWorkerId,
                PiecesInPeriod = activity.GetValueOrDefault(p.Id)?.CompletedPieces ?? 0,
                DaysWorkedInPeriod = activity.GetValueOrDefault(p.Id)?.DaysWorked ?? 0,
                WorkerIds = activity.GetValueOrDefault(p.Id)?.WorkerIds ?? new HashSet<int>(),
                Stages = p.Stages
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                    .Select((s, index) => new StageRow
                    {
                        StageId = s.Id,
                        StageName = s.StageName,
                        PiecesPerWorkday = s.PiecesPerWorkday,
                        SortOrder = s.SortOrder,
                        IsActive = s.IsActive,
                        IsRackingStage = s.IsRackingStage,
                        // الرقم المعروض = موقعها في الخط، مش قيمة SortOrder
                        // (اللي ممكن يكون فيها فجوات من تعديلات قديمة)
                        DisplayOrder = index + 1,
                        QualifiedWorkersCount = qualifiedCountByStage.GetValueOrDefault(s.Id),
                        DifficultyMultiplier = s.DifficultyMultiplier
                    }).ToList()
            }).ToList();

            ApplyFilter();
            RefreshSummary();
        }

        /// <summary>
        /// تطبيق البحث والفلاتر على القائمة المحمّلة (في الذاكرة — عدد
        /// المنتجات صغير). كل الفلاتر بتتجمع بـ AND.
        /// </summary>
        private void ApplyFilter()
        {
            var query = SearchText.Trim();
            var selectedId = SelectedProduct?.ProductId;

            // "شغّالين" بقت تعني إنتاج فعلي في الفترة، مش فلاج IsActive
            IEnumerable<ProductRow> filtered = ActiveFilter switch
            {
                ProductFilter.All => _allProducts,
                ProductFilter.Inactive => _allProducts.Where(p => !p.IsActive),
                _ => _allProducts.Where(p => p.WorkedInPeriod)
            };

            if (query.Length > 0)
                filtered = filtered.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Stages.Any(s => s.StageName.Contains(query, StringComparison.OrdinalIgnoreCase)));

            // المنتجات اللي فيها مرحلة بالاسم ده (نفس اسم المرحلة بيتكرر
            // عبر منتجات، والمستخدم بيسأل "مين فيه لمعة؟")
            if (SelectedStageFilter?.StageName is { } stageName)
                filtered = filtered.Where(p =>
                    p.Stages.Any(s => s.IsActive && s.StageName == stageName));

            // المنتجات اللي العامل ده اشتغل عليها في الفترة
            if (SelectedWorkerFilter?.WorkerId is { } workerId)
                filtered = filtered.Where(p => p.WorkerIds.Contains(workerId));

            filtered = (SelectedVolumeFilter?.Sort ?? ProductVolumeSort.None) switch
            {
                ProductVolumeSort.HighestFirst =>
                    filtered.OrderByDescending(p => p.PiecesInPeriod).ThenBy(p => p.Name),
                ProductVolumeSort.LowestFirst =>
                    filtered.OrderBy(p => p.PiecesInPeriod).ThenBy(p => p.Name),
                _ => filtered
            };

            Products.Clear();
            foreach (var p in filtered) Products.Add(p);

            // الحفاظ على الاختيار الحالي لو لسه موجود بعد الفلترة
            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selectedId)
                ?? Products.FirstOrDefault();

            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
            OnPropertyChanged(nameof(HasExtraFilters));
            OnPropertyChanged(nameof(ActiveFilterCount));
        }

        /// <summary>يعيد التحميل من غير ما يضيّع المنتج المفتوح</summary>
        private async Task ReloadKeepingSelectionAsync()
        {
            var selectedId = SelectedProduct?.ProductId;
            await LoadAsync();
            if (selectedId is not null)
                SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selectedId.Value)
                    ?? Products.FirstOrDefault();
        }

        // ------- إدارة المنتجات -------

        /// <summary>
        /// عمال الرص المتاحين لاختيارهم كعامل رص ثابت للمنتج — "بدون"
        /// أول عنصر دايمًا عشان يبقى الافتراضي وتقدر تشيل الاختيار.
        /// </summary>
        private async Task<List<RackingWorkerChoice>> LoadRackingWorkerChoicesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var workers = await scope.ServiceProvider
                .GetRequiredService<IWorkerRepository>()
                .GetAllWithSkillsAsync();

            var choices = new List<RackingWorkerChoice> { new(null, "بدون") };
            choices.AddRange(workers
                .Where(w => w.HourlyRole == HourlyRole.Racking)
                .OrderBy(w => w.SortOrder)
                .Select(w => new RackingWorkerChoice(w.Id, w.FullName)));

            return choices;
        }

        [RelayCommand]
        private async Task AddProductAsync()
        {
            var dialog = new ProductEditDialog(await LoadRackingWorkerChoicesAsync())
                { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                var created = await mgmt.CreateProductAsync(dialog.ProductName, dialog.ProductDescription);

                if (dialog.ImageData is not null)
                    await mgmt.SetProductImageAsync(created.Id, dialog.ImageData);

                if (dialog.RackingWorkerId is not null)
                    await mgmt.SetRackingWorkerAsync(created.Id, dialog.RackingWorkerId);

                await LoadAsync();
                // اختيار المنتج الجديد فورًا عشان المستخدم يبدأ يضيف مراحله
                SelectedProduct = Products.FirstOrDefault(p => p.ProductId == created.Id);
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة المنتج");
            }
        }

        [RelayCommand]
        private async Task EditProductAsync()
        {
            if (SelectedProduct is null) return;

            var dialog = new ProductEditDialog(await LoadRackingWorkerChoicesAsync())
                { Owner = Application.Current.MainWindow, Title = "تعديل منتج" };
            dialog.LoadProduct(SelectedProduct.Name,
                SelectedProduct.Description,
                SelectedProduct.ImageData,
                SelectedProduct.RackingWorkerId);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                await mgmt.UpdateProductAsync(SelectedProduct.ProductId,
                    dialog.ProductName, dialog.ProductDescription);

                // الصورة بتتحفظ بس لو المستخدم غيّرها فعلاً
                if (dialog.ImageChanged)
                    await mgmt.SetProductImageAsync(SelectedProduct.ProductId, dialog.ImageData);

                if (dialog.RackingWorkerId != SelectedProduct.RackingWorkerId)
                    await mgmt.SetRackingWorkerAsync(SelectedProduct.ProductId, dialog.RackingWorkerId);

                await ReloadKeepingSelectionAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في تعديل المنتج");
            }
        }

        [RelayCommand]
        private async Task ToggleProductActiveAsync()
        {
            if (SelectedProduct is null) return;

            var isDeactivating = SelectedProduct.IsActive;
            var message = isDeactivating
                ? $"إيقاف المنتج \"{SelectedProduct.Name}\"؟\nهيختفي هو ومراحله من شاشة التسجيل، وكل السجلات التاريخية هتفضل محفوظة."
                : $"إعادة تفعيل المنتج \"{SelectedProduct.Name}\"؟";

            if (!Notify.Ask(message, "تأكيد"))
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateProductAsync(SelectedProduct.ProductId);
            else
                await mgmt.ReactivateProductAsync(SelectedProduct.ProductId);

            await LoadAsync();
        }

        /// <summary>
        /// يشيل المنتج نهائيًا — غير الإيقاف. بيمر على بوابة كلمة السر
        /// وبيتسجّل في سجل العمليات، وسجلات إنتاجه القديمة بتفضل محفوظة.
        /// </summary>
        [RelayCommand]
        private async Task DeleteProductAsync()
        {
            if (SelectedProduct is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

                var input = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "حذف منتج",
                    $"{SelectedProduct.Name} — هيختفي هو ومراحله من كل القوايم. سجلات الإنتاج القديمة عليه هتفضل محفوظة.",
                    SensitiveActionKind.Delete,
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                var result = await mgmt.DeleteProductAsync(
                    SelectedProduct.ProductId, input.Password, input.Reason);

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

        // ------- إدارة المراحل -------

        [RelayCommand]
        private async Task AddStageAsync()
        {
            if (SelectedProduct is null) return;

            var dialog = new StageEditDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                await mgmt.AddStageAsync(SelectedProduct.ProductId,
                    dialog.StageName, dialog.PiecesPerWorkday, dialog.SortOrder, dialog.DifficultyMultiplier);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة المرحلة");
            }
        }

        /// <summary>
        /// يضيف مرحلة "رص" آخر خط الإنتاج — مرة واحدة بس لكل منتج
        /// (<see cref="ProductRow.HasRackingStage"/> بيقفل الزرار بعد كده).
        /// </summary>
        [RelayCommand]
        private async Task AddRackingStageAsync()
        {
            if (SelectedProduct is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProductManagementService>()
                    .AddRackingStageAsync(SelectedProduct.ProductId);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في إضافة مرحلة الرص");
            }
        }

        /// <summary>
        /// يعرض مين مؤهل للمرحلة دي بتقييماتهم، مرتبين من الأحسن للأضعف.
        ///
        /// النافذة نفسها بتنادي <c>SkillRatingService.GetRankedForStageAsync</c>
        /// — نفس الدالة اللي شاشة التسجيل اليومي شغالة بيها، عشان الترتيب
        /// اللي المدير بيشوفه هنا هو نفسه اللي هيلاقيه وهو بيسجّل.
        /// </summary>
        [RelayCommand]
        private async Task ShowQualifiedWorkersAsync(StageRow? stage)
        {
            if (stage is null) return;

            await QualifiedWorkersDialog.ShowAsync(
                Application.Current.MainWindow, _scopeFactory,
                stage.StageId, SelectedProduct?.Name ?? "", stage.StageName);
        }

        [RelayCommand]
        private async Task EditStageAsync(StageRow? stage)
        {
            if (stage is null) return;

            var dialog = new StageEditDialog { Owner = Application.Current.MainWindow, Title = "تعديل مرحلة" };
            dialog.LoadStage(stage.StageName, stage.PiecesPerWorkday, stage.SortOrder, stage.DifficultyMultiplier);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                await mgmt.UpdateStageAsync(stage.StageId,
                    dialog.StageName, dialog.PiecesPerWorkday, dialog.SortOrder ?? stage.SortOrder,
                    dialog.DifficultyMultiplier);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                Notify.Warn(ex.Message, "خطأ في تعديل المرحلة");
            }
        }

        /// <summary>يحرّك المرحلة خطوة لفوق في خط الإنتاج</summary>
        [RelayCommand]
        private Task MoveStageUpAsync(StageRow? stage) => MoveStageAsync(stage, moveUp: true);

        /// <summary>يحرّك المرحلة خطوة لتحت في خط الإنتاج</summary>
        [RelayCommand]
        private Task MoveStageDownAsync(StageRow? stage) => MoveStageAsync(stage, moveUp: false);

        private async Task MoveStageAsync(StageRow? stage, bool moveUp)
        {
            if (stage is null) return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();

            // القاعدة نفسها في الخدمة — الشاشة بتطلب الحركة بس
            if (!await mgmt.MoveStageAsync(stage.StageId, moveUp)) return;

            await ReloadKeepingSelectionAsync();
        }

        [RelayCommand]
        private async Task ToggleStageActiveAsync(StageRow? stage)
        {
            if (stage is null) return;

            var isDeactivating = stage.IsActive;
            var message = isDeactivating
                ? $"إيقاف مرحلة \"{stage.StageName}\"؟\nهتختفي من شاشة التسجيل وسجلاتها التاريخية هتفضل محفوظة."
                : $"إعادة تفعيل مرحلة \"{stage.StageName}\"؟";

            if (!Notify.Ask(message, "تأكيد"))
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateStageAsync(stage.StageId);
            else
                await mgmt.ReactivateStageAsync(stage.StageId);

            await LoadAsync();
        }

        /// <summary>يشيل مرحلة من الخط نهائيًا — بكلمة سر وسبب مسجّلين</summary>
        [RelayCommand]
        private async Task DeleteStageAsync(StageRow? stage)
        {
            if (stage is null) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var gate = scope.ServiceProvider.GetRequiredService<OperationsPasswordService>();

                var input = SensitiveActionDialog.Ask(
                    Application.Current.MainWindow,
                    "حذف مرحلة",
                    $"{stage.StageName} — هتختفي من الخط. سجلات الإنتاج عليها وأجور العمال هتفضل زي ما هي.",
                    SensitiveActionKind.Delete,
                    await gate.IsConfiguredAsync());

                if (input is null) return;

                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                var result = await mgmt.DeleteStageAsync(stage.StageId, input.Password, input.Reason);

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
    }
}
