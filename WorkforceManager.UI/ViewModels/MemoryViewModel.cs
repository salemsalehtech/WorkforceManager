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

        /// <summary>الخطط اللي لسه ما اتنفّذتش (بعد فلترة البحث)</summary>
        public ObservableCollection<ProductionMemoryDto> Active { get; } = new();

        /// <summary>اللي اتبدأ فعلاً — للمرجع بس، مفيش تعديل عليها (بعد فلترة البحث)</summary>
        public ObservableCollection<ProductionMemoryDto> Completed { get; } = new();

        /// <summary>كل الخطط قبل الفلترة — بترجع منها Active/Completed كل ما البحث يتغيّر</summary>
        private List<ProductionMemoryDto> _allActive = new();
        private List<ProductionMemoryDto> _allCompleted = new();

        [ObservableProperty] private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

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
        [NotifyPropertyChangedFor(nameof(FormTitle))]
        private int? _editingId;

        public bool IsEditing => EditingId is not null;

        public string SaveButtonText => IsEditing ? "احفظ التعديل" : "أضف للذاكرة";

        /// <summary>عنوان الفورم — كان نص ثابت "خطة جديدة" حتى وانت بتعدّل خطة موجودة، ده كان بيلخبط</summary>
        public string FormTitle => IsEditing ? "تعديل خطة" : "خطة جديدة";

        /// <summary>
        /// لقطة الفورم وقت آخر مرة اتحمّل فيها (فتح تعديل، أو ClearForm) —
        /// بنقارن بيها عشان نعرف لو المستخدم غيّر حاجة لسه ماحفظهاش، قبل
        /// ما نفقدها بالتنقل لخطة تانية أو الإلغاء.
        /// </summary>
        private int? _snapshotProductId;
        private string _snapshotNotes = string.Empty;
        private DateTime _snapshotRemindOn;
        private List<int> _snapshotStageOrder = new();

        private void TakeSnapshot()
        {
            _snapshotProductId = SelectedProduct?.ProductId;
            _snapshotNotes = Notes;
            _snapshotRemindOn = RemindOn;
            _snapshotStageOrder = new List<int>(StageOrder);
        }

        private bool HasUnsavedFormChanges =>
            SelectedProduct?.ProductId != _snapshotProductId ||
            Notes != _snapshotNotes ||
            RemindOn != _snapshotRemindOn ||
            !StageOrder.SequenceEqual(_snapshotStageOrder);

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
        partial void OnSelectedProductChanged(MemoryProductOption? value)
        {
            StageOrder = new List<int>();
            ProductError = "";
        }

        /// <summary>خطأ خانة المنتج وقت الحفظ — بيتعرض تحتها بـFieldError</summary>
        [ObservableProperty] private string _productError = "";

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

            _allActive = (await service.GetActiveAsync()).ToList();
            _allCompleted = (await service.GetCompletedAsync()).ToList();
            ApplyFilter();
        }

        private async Task ReloadListsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            await ReloadListsAsync(scope);
        }

        /// <summary>بيبني Active/Completed المعروضتين من القايمتين الكاملتين حسب نص البحث — نفس نمط ProductsViewModel.ApplyFilter</summary>
        private void ApplyFilter()
        {
            var query = SearchText.Trim();

            IEnumerable<ProductionMemoryDto> active = _allActive;
            IEnumerable<ProductionMemoryDto> completed = _allCompleted;

            if (query.Length > 0)
            {
                active = active.Where(m => m.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase));
                completed = completed.Where(m => m.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            Active.Clear();
            foreach (var memory in active) Active.Add(memory);

            Completed.Clear();
            foreach (var memory in completed) Completed.Add(memory);
        }

        /// <summary>
        /// بيفتح نافذة الترتيب. الواجهة بتناديها لأن الدايالوج محتاج
        /// نافذة أب — الـ ViewModel بيجهّز المدخلات ويستقبل النتيجة بس.
        /// </summary>
        public IReadOnlyList<(int StageId, string StageName)> StagesForOrdering() =>
            SelectedProduct?.Stages.Select(s => (s.StageId, s.StageName)).ToList()
                ?? new List<(int, string)>();

        public void ApplyStageOrder(IReadOnlyList<int> order) => StageOrder = order.ToList();

        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task SaveAsync()
        {
            ProductError = FieldRules.Required(SelectedProduct, "اختار المنتج الأول");
            if (SelectedProduct is null) return;

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

            // تبديل لخطة تانية وانت لسه في نص تعديل خطة ماحفظتهاش بيفقد
            // اللي اتغيّر من غير تحذير — نفس الحماية اللي في شاشة التسجيل
            // اليومي (FlowSessionViewModel.HasUserInput)
            if (IsEditing && EditingId != memory.Id && HasUnsavedFormChanges &&
                !Notify.Ask("عندك تعديلات لسه ماحفظتهاش على الخطة دي، هتضيع لو فتحت خطة تانية. متأكد؟", "تأكيد"))
                return;

            EditingId = memory.Id;
            SelectedProduct = Products.FirstOrDefault(p => p.ProductId == memory.ProductId);

            // SelectedProduct بيصفّر الترتيب، فالترتيب بيتحط بعده
            StageOrder = memory.Stages.Select(s => s.ProductionStageId).ToList();
            Notes = memory.Notes;
            RemindOn = memory.RemindOn;

            TakeSnapshot();
        }

        [RelayCommand]
        private void CancelEdit()
        {
            if (HasUnsavedFormChanges &&
                !Notify.Ask("عندك تعديلات لسه ماحفظتهاش، هتضيع لو ألغيت. متأكد؟", "تأكيد"))
                return;

            ClearForm();
        }

        /// <summary>بدء تنفيذ الخطة من كارتها في الشاشة مباشرة — بدل ما يستنى تذكير بدء التشغيل</summary>
        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task StartNowAsync(ProductionMemoryDto? memory)
        {
            if (memory is null || !memory.CanStart) return;

            await App.StartMemorySessionAsync(memory);
            await ReloadListsAsync();
        }

        /// <summary>تأجيل سريع من الكارت — بينادى من MemoryView.xaml.cs بعد ما نافذة اختيار التاريخ ترجّع قيمة</summary>
        public async Task PostponeAsync(int id, DateTime newRemindOn)
        {
            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ProductionMemoryService>()
                    .PostponeAsync(id, newRemindOn);

            await ReloadListsAsync();
            Notify.Success("اتأجّل التذكير");
        }

        /// <summary>
        /// خطة اتعلّمت منجزة غلط (مثلاً فتح شاشتها من التذكير من غير ما يتسجّل
        /// فيها إنتاج فعلي) — بترجعها نشطة تاني.
        /// </summary>
        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task ReactivateAsync(ProductionMemoryDto? memory)
        {
            if (memory is null) return;

            using (var scope = _scopeFactory.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ProductionMemoryService>()
                    .ReactivateAsync(memory.Id);

            await ReloadListsAsync();
            Notify.Success("الخطة رجعت نشطة");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
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

            TakeSnapshot();
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
