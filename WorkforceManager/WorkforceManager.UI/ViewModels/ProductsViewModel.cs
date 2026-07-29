using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة المنتجات والمراحل: قائمة المنتجات (مع بحث وإظهار
    /// الموقوف)، ولوحة تفاصيل بتعرض مراحل المنتج المحدد بكوتاتها،
    /// مع كل عمليات الإدارة: إضافة/تعديل/إيقاف منتج أو مرحلة.
    /// تعديل الكوتة بيسري على التسجيلات الجديدة فقط — القديم محمي
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

        /// <summary>الفلتر السريع المختار</summary>
        [ObservableProperty]
        private ProductFilter _activeFilter = ProductFilter.Active;

        partial void OnActiveFilterChanged(ProductFilter value)
        {
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterInactive));
            ApplyFilter();
        }

        public bool IsFilterActive => ActiveFilter == ProductFilter.Active;
        public bool IsFilterAll => ActiveFilter == ProductFilter.All;
        public bool IsFilterInactive => ActiveFilter == ProductFilter.Inactive;

        [RelayCommand]
        private void SetFilter(string? filter) =>
            ActiveFilter = filter switch
            {
                "all" => ProductFilter.All,
                "inactive" => ProductFilter.Inactive,
                _ => ProductFilter.Active
            };

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        // ------- عدّادات الملخص -------

        public int TotalProducts => _allProducts.Count;
        public int ActiveProducts => _allProducts.Count(p => p.IsActive);
        public int TotalStages => _allProducts.Where(p => p.IsActive).Sum(p => p.ActiveStagesCount);

        /// <summary>منتجات فيها مشكلة تمنع الإنتاج عليها فعليًا</summary>
        public int NeedsAttentionCount => _allProducts.Count(p => p.IsActive && p.NeedsAttention);

        public string ResultsText => Products.Count == TotalProducts
            ? $"{Products.Count} منتج"
            : $"{Products.Count} من {TotalProducts}";

        public bool NoResults => Products.Count == 0 && _allProducts.Count > 0;

        private void RefreshSummary()
        {
            OnPropertyChanged(nameof(TotalProducts));
            OnPropertyChanged(nameof(ActiveProducts));
            OnPropertyChanged(nameof(TotalStages));
            OnPropertyChanged(nameof(NeedsAttentionCount));
            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
        }

        /// <summary>يعرض المنتجات اللي فيها مشكلة بس</summary>
        [RelayCommand]
        private void ShowNeedsAttention()
        {
            SearchText = string.Empty;
            ActiveFilter = ProductFilter.Active;

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

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var productRepo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var skillRepo = scope.ServiceProvider.GetRequiredService<IGenericRepository<WorkerSkill>>();

            // عدد العمال المؤهلين لكل مرحلة — استعلام واحد وتجميع في الذاكرة.
            // ده اللي بيكشف المرحلة اللي "مفيش حد يعرف يعملها"، وهي مشكلة
            // بتوقف الإنتاج فعليًا لأن رحلة الإنتاج بتعرض المؤهلين بس.
            var qualifiedCountByStage = (await skillRepo.GetAllAsync())
                .GroupBy(ws => ws.ProductionStageId)
                .ToDictionary(g => g.Key, g => g.Count());

            var products = await productRepo.GetAllWithStagesAsync();
            _allProducts = products.Select(p => new ProductRow
            {
                ProductId = p.Id,
                Name = p.Name,
                ProductCode = p.ProductCode ?? "—",
                Description = p.Description ?? "",
                IsActive = p.IsActive,
                ImageData = p.ImageData,
                Stages = p.Stages
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                    .Select((s, index) => new StageRow
                    {
                        StageId = s.Id,
                        StageName = s.StageName,
                        PiecesPerWorkday = s.PiecesPerWorkday,
                        SortOrder = s.SortOrder,
                        IsActive = s.IsActive,
                        // الرقم المعروض = موقعها في الخط، مش قيمة SortOrder
                        // (اللي ممكن يكون فيها فجوات من تعديلات قديمة)
                        DisplayOrder = index + 1,
                        QualifiedWorkersCount = qualifiedCountByStage.GetValueOrDefault(s.Id)
                    }).ToList()
            }).ToList();

            ApplyFilter();
            RefreshSummary();
        }

        /// <summary>تطبيق البحث والفلتر على القائمة المحمّلة (في الذاكرة — عدد المنتجات صغير)</summary>
        private void ApplyFilter()
        {
            var query = SearchText.Trim();
            var selectedId = SelectedProduct?.ProductId;

            IEnumerable<ProductRow> filtered = ActiveFilter switch
            {
                ProductFilter.All => _allProducts,
                ProductFilter.Inactive => _allProducts.Where(p => !p.IsActive),
                _ => _allProducts.Where(p => p.IsActive)
            };

            if (query.Length > 0)
                filtered = filtered.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Stages.Any(s => s.StageName.Contains(query, StringComparison.OrdinalIgnoreCase)));

            Products.Clear();
            foreach (var p in filtered) Products.Add(p);

            // الحفاظ على الاختيار الحالي لو لسه موجود بعد الفلترة
            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selectedId)
                ?? Products.FirstOrDefault();

            OnPropertyChanged(nameof(ResultsText));
            OnPropertyChanged(nameof(NoResults));
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

        [RelayCommand]
        private async Task AddProductAsync()
        {
            var dialog = new ProductEditDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                var created = await mgmt.CreateProductAsync(dialog.ProductName, dialog.ProductCode, dialog.ProductDescription);

                if (dialog.ImageData is not null)
                    await mgmt.SetProductImageAsync(created.Id, dialog.ImageData);

                await LoadAsync();
                // اختيار المنتج الجديد فورًا عشان المستخدم يبدأ يضيف مراحله
                SelectedProduct = Products.FirstOrDefault(p => p.ProductId == created.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في إضافة المنتج", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task EditProductAsync()
        {
            if (SelectedProduct is null) return;

            var dialog = new ProductEditDialog { Owner = Application.Current.MainWindow, Title = "تعديل منتج" };
            dialog.LoadProduct(SelectedProduct.Name,
                SelectedProduct.ProductCode == "—" ? null : SelectedProduct.ProductCode,
                SelectedProduct.Description,
                SelectedProduct.ImageData);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                await mgmt.UpdateProductAsync(SelectedProduct.ProductId,
                    dialog.ProductName, dialog.ProductCode, dialog.ProductDescription);

                // الصورة بتتحفظ بس لو المستخدم غيّرها فعلاً
                if (dialog.ImageChanged)
                    await mgmt.SetProductImageAsync(SelectedProduct.ProductId, dialog.ImageData);

                await ReloadKeepingSelectionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في تعديل المنتج", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (MessageBox.Show(message, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateProductAsync(SelectedProduct.ProductId);
            else
                await mgmt.ReactivateProductAsync(SelectedProduct.ProductId);

            await LoadAsync();
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
                    dialog.StageName, dialog.PiecesPerWorkday, dialog.SortOrder);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في إضافة المرحلة", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task EditStageAsync(StageRow? stage)
        {
            if (stage is null) return;

            var dialog = new StageEditDialog { Owner = Application.Current.MainWindow, Title = "تعديل مرحلة" };
            dialog.LoadStage(stage.StageName, stage.PiecesPerWorkday, stage.SortOrder);
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();
                await mgmt.UpdateStageAsync(stage.StageId,
                    dialog.StageName, dialog.PiecesPerWorkday, dialog.SortOrder ?? stage.SortOrder);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "خطأ في تعديل المرحلة", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (MessageBox.Show(message, "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = _scopeFactory.CreateScope();
            var mgmt = scope.ServiceProvider.GetRequiredService<ProductManagementService>();

            if (isDeactivating)
                await mgmt.DeactivateStageAsync(stage.StageId);
            else
                await mgmt.ReactivateStageAsync(stage.StageId);

            await LoadAsync();
        }
    }

    // ------- نماذج العرض الخاصة بالشاشة -------

    /// <summary>الفلتر السريع فوق قائمة المنتجات</summary>
    public enum ProductFilter { Active, All, Inactive }

    /// <summary>منتج واحد في قائمة الشاشة، بمراحله المحمّلة معاه</summary>
    public class ProductRow
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public string ProductCode { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsActive { get; init; }
        public List<StageRow> Stages { get; init; } = new();

        /// <summary>صورة المنتج المخزّنة (null = مفيش صورة)</summary>
        public byte[]? ImageData { get; init; }

        /// <summary>
        /// الصورة جاهزة للعرض. بتتبني مرة واحدة مع بناء الصف مش مع كل
        /// رسم للبطاقة — فك تشفير الصورة في كل مرة كان هيتقل القائمة.
        /// </summary>
        public System.Windows.Media.ImageSource? Image => _image ??= ProductImageHelper.ToImageSource(ImageData);
        private System.Windows.Media.ImageSource? _image;

        /// <summary>عنده صورة؟ (لو لأ بتظهر دايرة الحروف الأولى مكانها)</summary>
        public bool HasImage => ImageData is { Length: > 0 };

        public string StatusText => IsActive ? "نشط" : "موقوف";
        public int ActiveStagesCount => Stages.Count(s => s.IsActive);
        public string StagesCountText => $"{ActiveStagesCount} مرحلة";

        /// <summary>أول حرفين من اسم المنتج — للدايرة على البطاقة</summary>
        public string Initials
        {
            get
            {
                var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "؟";
                return parts.Length == 1
                    ? parts[0][..Math.Min(2, parts[0].Length)]
                    : $"{parts[0][0]}{parts[1][0]}";
            }
        }

        /// <summary>إجمالي كوتة الخط: مجموع كوتات المراحل النشطة (مؤشر سرعة الخط)</summary>
        public int TotalQuota => Stages.Where(s => s.IsActive).Sum(s => s.PiecesPerWorkday);

        // ------- التنبيهات -------

        /// <summary>منتج من غير أي مرحلة نشطة — مينفعش يتسجل عليه إنتاج خالص</summary>
        public bool HasNoStages => ActiveStagesCount == 0;

        /// <summary>مراحل نشطة مفيش حد مؤهل ليها — الخط بيقف عندها</summary>
        public int UncoveredStagesCount => Stages.Count(s => s.IsActive && s.HasNoQualifiedWorkers);

        public bool NeedsAttention => HasNoStages || UncoveredStagesCount > 0;

        public string AttentionText => HasNoStages
            ? "مفيش مراحل — مينفعش يتسجل عليه إنتاج"
            : UncoveredStagesCount > 0
                ? $"{UncoveredStagesCount} مرحلة مفيش حد مؤهل ليها"
                : "";
    }

    /// <summary>مرحلة واحدة في خط إنتاج المنتج المحدد</summary>
    public class StageRow
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";
        public int PiecesPerWorkday { get; init; }
        public int SortOrder { get; init; }
        public bool IsActive { get; init; }

        /// <summary>ترتيبها المعروض في الخط (1، 2، 3...) — محسوب من موقعها مش من SortOrder</summary>
        public int DisplayOrder { get; init; }

        /// <summary>عدد العمال المربوط لهم المهارة دي</summary>
        public int QualifiedWorkersCount { get; init; }

        public string StatusText => IsActive ? "نشطة" : "موقوفة";
        public string QuotaText => $"{PiecesPerWorkday} قطعة / يومية";

        /// <summary>
        /// مرحلة نشطة مفيش ولا عامل مؤهل ليها. دي مشكلة حقيقية بتوقف
        /// الخط: شاشة رحلة الإنتاج بتعرض المؤهلين بس، فالمرحلة دي مش
        /// هيتوزع عليها حد ومش هينفع تتسجل.
        /// </summary>
        public bool HasNoQualifiedWorkers => IsActive && QualifiedWorkersCount == 0;

        public string WorkersText => QualifiedWorkersCount == 0
            ? "مفيش عمال مؤهلين"
            : $"{QualifiedWorkersCount} عامل مؤهل";
    }
}
