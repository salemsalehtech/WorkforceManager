using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الذاكرة": خطط الإنتاج المتأجّلة.
    ///
    /// الشاشة دي مبتسجّلش إنتاج ولا بتلمس أي رقم — كل اللي بتعمله إنها
    /// تكتب نية: "المنتج ده، بالترتيب ده، يوم كذا". التنفيذ بيحصل في
    /// شاشة الإنتاج اليومي زي أي يوم عادي، الفرق الوحيد إن ترتيب النطاقات
    /// بياخد ترتيب الخطة (شوف ProductionLine.CustomOrder).
    /// </summary>
    public partial class MemoryViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public MemoryViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>الخطط اللي لسه ما اتنفّذتش</summary>
        public ObservableCollection<ProductionMemoryDto> Active { get; } = new();

        /// <summary>اللي اتبدأ فعلاً — للمرجع بس، مفيش تعديل عليها</summary>
        public ObservableCollection<ProductionMemoryDto> Completed { get; } = new();

        /// <summary>منتجات ينفع تتعمل عليها خطة (نشطة وليها مراحل)</summary>
        public ObservableCollection<MemoryProductOption> Products { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedProduct))]
        [NotifyPropertyChangedFor(nameof(StageOrderSummary))]
        private MemoryProductOption? _selectedProduct;

        public bool HasSelectedProduct => SelectedProduct is not null;

        [ObservableProperty] private string _notes = string.Empty;

        [ObservableProperty] private DateTime _remindOn = DateTime.Today.AddDays(1);

        /// <summary>الترتيب المختار للخطة اللي بتتكتب دلوقتي</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StageOrderSummary))]
        private List<int> _stageOrder = new();

        /// <summary>معرّف الخطة اللي بتتعدّل — null معناها خطة جديدة</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditing))]
        [NotifyPropertyChangedFor(nameof(SaveButtonText))]
        private int? _editingId;

        public bool IsEditing => EditingId is not null;

        public string SaveButtonText => IsEditing ? "احفظ التعديل" : "أضف للذاكرة";

        public string StageOrderSummary
        {
            get
            {
                if (SelectedProduct is null) return "اختار منتج الأول";
                if (StageOrder.Count == 0) return "كل المراحل بترتيب المنتج";

                var names = SelectedProduct.Stages
                    .Where(s => StageOrder.Contains(s.StageId))
                    .OrderBy(s => StageOrder.IndexOf(s.StageId))
                    .Select(s => s.StageName);

                return string.Join("  ←  ", names);
            }
        }

        [ObservableProperty] private bool _isBusy;

        /// <summary>
        /// اختيار منتج تاني بيصفّر الترتيب: مراحل المنتج القديم مالهاش
        /// أي معنى مع المنتج الجديد، وسيبانها كانت هترمي عند الحفظ
        /// </summary>
        partial void OnSelectedProductChanged(MemoryProductOption? value) => StageOrder = new List<int>();

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var products = await scope.ServiceProvider
                    .GetRequiredService<IProductRepository>().GetActiveWithStagesAsync();

                var options = products
                    .Select(p => new MemoryProductOption
                    {
                        ProductId = p.Id,
                        ProductName = p.Name,
                        Stages = ProductionLine.Active(p)
                            .Select(s => new MemoryStageOption { StageId = s.Id, StageName = s.StageName })
                            .ToList()
                    })
                    // منتج من غير مراحل نشطة مينفعش تتعمل عليه خطة أصلاً
                    .Where(p => p.Stages.Count > 0)
                    .ToList();

                Products.Clear();
                foreach (var option in options) Products.Add(option);

                await ReloadListsAsync(scope);
            }
            finally { IsBusy = false; }
        }

        private async Task ReloadListsAsync(IServiceScope scope)
        {
            var service = scope.ServiceProvider.GetRequiredService<ProductionMemoryService>();

            Active.Clear();
            foreach (var memory in await service.GetActiveAsync()) Active.Add(memory);

            Completed.Clear();
            foreach (var memory in await service.GetCompletedAsync()) Completed.Add(memory);
        }

        private async Task ReloadListsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            await ReloadListsAsync(scope);
        }

        /// <summary>
        /// بيفتح نافذة الترتيب. الواجهة بتناديها لأن الدايالوج محتاج
        /// نافذة أب — الـ ViewModel بيجهّز المدخلات ويستقبل النتيجة بس.
        /// </summary>
        public IReadOnlyList<(int StageId, string StageName)> StagesForOrdering() =>
            SelectedProduct?.Stages.Select(s => (s.StageId, s.StageName)).ToList()
                ?? new List<(int, string)>();

        public void ApplyStageOrder(IReadOnlyList<int> order) => StageOrder = order.ToList();

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (SelectedProduct is null)
            {
                Notify.Warn("اختار المنتج الأول");
                return;
            }

            // الترتيب الفاضي معناه "كل المراحل بترتيب المنتج" — أوضح
            // للمستخدم من إنه يتفرض عليه يفتح نافذة الترتيب لخطة عادية
            var order = StageOrder.Count > 0
                ? StageOrder
                : SelectedProduct.Stages.Select(s => s.StageId).ToList();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProductionMemoryService>();

                if (EditingId is { } id)
                    await service.UpdateAsync(id, SelectedProduct.ProductId, order, Notes, RemindOn);
                else
                    await service.CreateAsync(SelectedProduct.ProductId, order, Notes, RemindOn);

                await ReloadListsAsync(scope);
            }
            catch (InvalidOperationException ex)
            {
                Notify.Warn(ex.Message);
                return;
            }

            Notify.Success(IsEditing ? "الخطة اتعدّلت" : "الخطة اتحفظت في الذاكرة");
            ClearForm();
        }

        [RelayCommand]
        private void Edit(ProductionMemoryDto? memory)
        {
            if (memory is null) return;

            EditingId = memory.Id;
            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == memory.ProductId);

            // SelectedProduct بيصفّر الترتيب، فالترتيب بيتحط بعده
            StageOrder = memory.Stages.Select(s => s.ProductionStageId).ToList();
            Notes = memory.Notes;
            RemindOn = memory.RemindOn;
        }

        [RelayCommand]
        private void CancelEdit() => ClearForm();

        [RelayCommand]
        private async Task DeleteAsync(ProductionMemoryDto? memory)
        {
            if (memory is null) return;

            if (!Notify.AskDangerous(
                $"هتشيل خطة \"{memory.ProductName}\" من الذاكرة نهائيًا. متأكد؟", "حذف خطة"))
                return;

            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ProductionMemoryService>()
                    .DeleteAsync(memory.Id);

            if (EditingId == memory.Id) ClearForm();

            await ReloadListsAsync();
            Notify.Success("الخطة اتشالت");
        }

        private void ClearForm()
        {
            EditingId = null;
            SelectedProduct = null;
            StageOrder = new List<int>();
            Notes = string.Empty;
            RemindOn = DateTime.Today.AddDays(1);
        }
    }

    /// <summary>منتج في قايمة اختيار الخطة، بمراحله النشطة</summary>
    public class MemoryProductOption
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public List<MemoryStageOption> Stages { get; init; } = new();
    }

    public class MemoryStageOption
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = "";
    }
}
