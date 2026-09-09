using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// خطط الإنتاج المتأجّلة ("الذاكرة"): المستخدم بيكتب إنه ناوي يعمل
    /// منتج يوم كذا بترتيب مراحل معيّن، والبرنامج بيفكّره.
    ///
    /// الخدمة دي **مبتسجّلش أي إنتاج** — كل اللي بتعمله إنها تحفظ الخطة
    /// وترجّع ترتيبها. التسجيل نفسه بيمشي في ProductionFlowService زي أي
    /// جلسة عادية، وبياخد الترتيب كباراميتر.
    /// </summary>
    public class ProductionMemoryService
    {
        private readonly IProductionMemoryRepository _memories;
        private readonly IProductRepository _products;

        public ProductionMemoryService(
            IProductionMemoryRepository memories, IProductRepository products)
        {
            _memories = memories;
            _products = products;
        }

        /// <summary>الخطط النشطة، الأقرب تذكيرًا الأول</summary>
        public async Task<IReadOnlyList<ProductionMemoryDto>> GetActiveAsync() =>
            (await _memories.GetActiveAsync()).Select(ToDto).ToList();

        /// <summary>الخطط المنجزة (قايمة مرجعية للقراءة بس)</summary>
        public async Task<IReadOnlyList<ProductionMemoryDto>> GetCompletedAsync() =>
            (await _memories.GetCompletedAsync()).Select(ToDto).ToList();

        /// <summary>
        /// الخطط اللي تذكيرها حان (اليوم ده أو قبله).
        ///
        /// <paramref name="today"/> باراميتر مش DateTime.Today جوه:
        /// نفس نمط DailyOperationsSignOffService — الاختبار محتاج يمشّي
        /// الوقت من غير ما يلمس ساعة الجهاز.
        /// </summary>
        public async Task<IReadOnlyList<ProductionMemoryDto>> GetDueAsync(DateTime today) =>
            (await _memories.GetDueAsync(today)).Select(ToDto).ToList();

        public async Task<ProductionMemoryDto?> GetAsync(int id)
        {
            var memory = await _memories.GetWithStagesAsync(id);
            return memory is null ? null : ToDto(memory);
        }

        /// <summary>خطة جديدة. بيرجّع معرّفها.</summary>
        public async Task<int> CreateAsync(
            int productId, IReadOnlyList<int> stageIds, string notes, DateTime remindOn)
        {
            await ValidatePlanAsync(productId, stageIds);

            var memory = new ProductionMemory
            {
                ProductId = productId,
                Notes = (notes ?? string.Empty).Trim(),
                RemindOn = remindOn.Date,
                CreatedAt = DateTime.Now,
                Stages = BuildStages(stageIds)
            };

            await _memories.AddAsync(memory);
            await _memories.SaveChangesAsync();

            return memory.Id;
        }

        /// <summary>
        /// تعديل خطة نشطة. المراحل بتتبدّل بالكامل مش بتتعدّل صف صف —
        /// إعادة الترتيب بتغيّر كل المواقع أصلاً، والفهرس الفريد على
        /// (الخطة، الموقع) بيمنع التحديث الجزئي وهو نص طريقه.
        /// </summary>
        public async Task UpdateAsync(
            int id, int productId, IReadOnlyList<int> stageIds, string notes, DateTime remindOn)
        {
            var memory = await _memories.GetWithStagesAsync(id)
                ?? throw new InvalidOperationException("الخطة المحددة مش موجودة");

            if (memory.CompletedAt is not null)
                throw new InvalidOperationException("الخطة دي اتنفّذت خلاص — مش هينفع تتعدّل");

            await ValidatePlanAsync(productId, stageIds);

            memory.ProductId = productId;
            memory.Notes = (notes ?? string.Empty).Trim();
            memory.RemindOn = remindOn.Date;

            memory.Stages.Clear();
            foreach (var stage in BuildStages(stageIds))
                memory.Stages.Add(stage);

            await _memories.SaveChangesAsync();
        }

        /// <summary>
        /// تأجيل التذكير ليوم جديد. كل حاجة تانية (المنتج، الترتيب،
        /// الملاحظات) بتفضل زي ما هي، والخطة بتفضل نشطة.
        /// </summary>
        public async Task PostponeAsync(int id, DateTime newRemindOn)
        {
            var memory = await _memories.GetByIdAsync(id)
                ?? throw new InvalidOperationException("الخطة المحددة مش موجودة");

            if (memory.CompletedAt is not null)
                throw new InvalidOperationException("الخطة دي اتنفّذت خلاص — مفيش تذكير يتأجّل");

            memory.RemindOn = newRemindOn.Date;
            await _memories.SaveChangesAsync();
        }

        /// <summary>
        /// نقل الخطة لقايمة المنجزة. بيتنادى **بمجرد فتح شاشة الإنتاج**
        /// من التذكير، مش بعد الحفظ (قرار مؤكد): التذكير شغله يفكّر، وهو
        /// خلّص شغله لما وصّل المستخدم للشاشة. كده التذكير مبيلحّش تاني
        /// حتى لو المستخدم قرر ميسجّلش حاجة في الآخر.
        /// </summary>
        public async Task MarkStartedAsync(int id)
        {
            var memory = await _memories.GetByIdAsync(id)
                ?? throw new InvalidOperationException("الخطة المحددة مش موجودة");

            // لو اتنادت مرتين (نقرتين سريعتين) الأولى هي اللي بتتسجّل
            if (memory.CompletedAt is not null) return;

            memory.CompletedAt = DateTime.Now;
            await _memories.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var memory = await _memories.GetByIdAsync(id)
                ?? throw new InvalidOperationException("الخطة المحددة مش موجودة");

            _memories.Remove(memory);
            await _memories.SaveChangesAsync();
        }

        /// <summary>
        /// ترتيب المراحل المخزّن — الشكل اللي ProductionFlowService
        /// بياخده. بيرمي لو الخطة بقت مش صالحة للتشغيل، عشان الشاشة
        /// متفتحش جلسة مكسورة.
        /// </summary>
        public async Task<IReadOnlyList<int>> GetStageOrderForSessionAsync(int id)
        {
            var memory = await _memories.GetWithStagesAsync(id)
                ?? throw new InvalidOperationException("الخطة المحددة مش موجودة");

            if (BlockedReason(memory) is { } reason)
                throw new InvalidOperationException(reason);

            return memory.Stages.OrderBy(s => s.Position).Select(s => s.ProductionStageId).ToList();
        }

        // ======================= داخلي =======================

        private static List<ProductionMemoryStage> BuildStages(IReadOnlyList<int> stageIds) =>
            stageIds
                .Select((stageId, index) => new ProductionMemoryStage
                {
                    ProductionStageId = stageId,
                    Position = index
                })
                .ToList();

        /// <summary>
        /// التحقق من الخطة وقت الحفظ. بيعيد استخدام
        /// <see cref="ProductionLine.CustomOrder"/> نفسها اللي التسجيل
        /// بيستخدمها — قاعدة واحدة، مفيش نسخة تانية تسيب حاجة تعدّي هنا
        /// وترفضها هناك.
        /// </summary>
        private async Task ValidatePlanAsync(int productId, IReadOnlyList<int> stageIds)
        {
            var product = await _products.GetWithStagesAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            if (product.IsDeleted)
                throw new InvalidOperationException("المنتج المحدد متشال");

            ProductionLine.CustomOrder(ProductionLine.Active(product), stageIds);
        }

        /// <summary>
        /// ليه الخطة دي مش صالحة للتشغيل — أو null لو صالحة.
        ///
        /// كل الأسباب مشتقة من حالة المنتج **دلوقتي**، مش من لقطة وقت
        /// الحفظ: المنتج ممكن يتوقف أو مراحله تتغيّر بعدها بشهر.
        /// </summary>
        private static string? BlockedReason(ProductionMemory memory)
        {
            if (memory.Product is null || memory.Product.IsDeleted)
                return "المنتج بتاع الخطة دي اتشال من النظام";

            if (!memory.Product.IsActive)
                return "المنتج بتاع الخطة دي موقوف — فعّله من شاشة المنتجات الأول";

            var activeIds = ProductionLine.Active(memory.Product).Select(s => s.Id).ToHashSet();
            var planned = memory.Stages.Select(s => s.ProductionStageId).ToList();

            if (planned.Count == 0)
                return "الخطة دي مالهاش مراحل";

            if (planned.Any(id => !activeIds.Contains(id)))
                return "فيه مراحل في الخطة دي اتوقفت أو اتشالت — عدّل الخطة الأول";

            return null;
        }

        private static ProductionMemoryDto ToDto(ProductionMemory memory)
        {
            var activeIds = memory.Product is null
                ? new HashSet<int>()
                : ProductionLine.Active(memory.Product).Select(s => s.Id).ToHashSet();

            return new ProductionMemoryDto
            {
                Id = memory.Id,
                ProductId = memory.ProductId,
                ProductName = memory.Product?.Name ?? "(منتج متشال)",
                Notes = memory.Notes,
                RemindOn = memory.RemindOn,
                CompletedAt = memory.CompletedAt,
                BlockedReason = BlockedReason(memory),
                Stages = memory.Stages
                    .OrderBy(s => s.Position)
                    .Select(s => new ProductionMemoryStageDto
                    {
                        ProductionStageId = s.ProductionStageId,
                        StageName = s.ProductionStage?.StageName ?? "(مرحلة متشالة)",
                        Position = s.Position + 1,
                        IsStillInLine = activeIds.Contains(s.ProductionStageId)
                    })
                    .ToList()
            };
        }
    }
}
