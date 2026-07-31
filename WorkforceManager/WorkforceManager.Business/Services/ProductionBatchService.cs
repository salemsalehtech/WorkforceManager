using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قواعد دفعات الإنتاج — المكان الوحيد اللي بيقرر فيه إن دفعة تتفتح،
    /// تتحرّك في الخط، تتقسم، أو تتقفل. أي شاشة أو خدمة تانية بتعدّي من هنا.
    ///
    /// القاعدة الأساسية: **القطعة عمرها ما تتعد مرتين ولا تظهر من العدم**.
    /// عشان كده كل نطاق إنتاج لازم يكون إما:
    ///   - بيبدأ من أول مرحلة في الخط  →  دفعة جديدة بتتفتح
    ///   - بيبدأ من نص الخط            →  لازم يكمّل دفعة مفتوحة، من المرحلة
    ///                                     اللي وقفت عندها بالظبط
    ///
    /// التكميل الجزئي: لو الدفعة 300 وكمّلنا 200، الـ200 بتفضل في نفس الدفعة
    /// (بتحافظ على هويتها وتاريخ بدايتها) والـ100 بتخرج في دفعة جديدة واقفة
    /// عند نفس المرحلة. كده الاتنين قابلين للتتبع كل واحد لوحده.
    /// </summary>
    public class ProductionBatchService
    {
        private readonly IProductionBatchRepository _batchRepo;
        private readonly IProductRepository _productRepo;

        public ProductionBatchService(
            IProductionBatchRepository batchRepo,
            IProductRepository productRepo)
        {
            _batchRepo = batchRepo;
            _productRepo = productRepo;
        }

        // ======================= استعلامات =======================

        /// <summary>الدفعات الواقفة لمنتج معين، جاهزة لعرضها في شاشة التسجيل</summary>
        public async Task<IReadOnlyList<OpenBatchDto>> GetOpenBatchesAsync(int productId, DateTime asOf)
        {
            var product = await _productRepo.GetWithStagesAsync(productId);
            if (product is null) return Array.Empty<OpenBatchDto>();

            var line = ActiveLine(product);
            var batches = await _batchRepo.GetOpenByProductAsync(productId);

            return batches
                .Select(b => Describe(b, product.Name, line, asOf))
                .Where(dto => dto is not null)
                .Select(dto => dto!)
                .ToList();
        }

        /// <summary>كل الواقف في المصنع (لشاشة إقفال اليوم)</summary>
        public async Task<IReadOnlyList<ParkedLotWithProductDto>> GetAllParkedAsync(DateTime asOf)
        {
            var batches = await _batchRepo.GetOpenAsOfAsync(asOf);
            var products = await _productRepo.GetAllWithStagesAsync();
            var lineByProduct = products.ToDictionary(p => p.Id, ActiveLine);

            var lots = new List<ParkedLotWithProductDto>();
            foreach (var batch in batches)
            {
                if (!lineByProduct.TryGetValue(batch.ProductId, out var line)) continue;

                var next = NextStage(batch, line);
                lots.Add(new ParkedLotWithProductDto
                {
                    BatchId = batch.Id,
                    ProductName = batch.Product.Name,
                    Quantity = batch.Quantity,
                    NextStageName = next?.StageName ?? "—",
                    StartedDate = batch.StartedDate,
                    DaysWaiting = DaysBetween(batch.StartedDate, asOf)
                });
            }

            return lots;
        }

        // ======================= تحريك الدفعات =======================

        /// <summary>
        /// بيتحقق من نطاق قبل ما يتسجل عليه أي إنتاج، ويرجّع الدفعة اللي
        /// هيحرّكها. بيرمي <see cref="InvalidOperationException"/> برسالة
        /// عربية واضحة لو النطاق مش سليم.
        ///
        /// **بيتنادى جوه ترانزاكشن الحفظ** — التحقق والكتابة لازم يكونوا
        /// تحت نفس القفل عشان متجش تسجيلتين يكمّلوا نفس الدفعة.
        /// </summary>
        public async Task<ProductionBatch> ResolveForRangeAsync(
            Product product, BatchRangeDto range, DateTime date)
        {
            var line = ActiveLine(product);
            if (line.Count == 0)
                throw new InvalidOperationException($"المنتج \"{product.Name}\" مالوش مراحل نشطة");

            var fromIndex = line.FindIndex(s => s.Id == range.FromStageId);
            if (fromIndex < 0)
                throw new InvalidOperationException("مرحلة بداية النطاق مش موجودة في خط المنتج");

            if (range.PieceCount <= 0)
                throw new InvalidOperationException("عدد القطع يجب أن يكون أكبر من صفر");

            // ---------- نطاق بيبدأ من أول الخط = دفعة جديدة ----------
            if (fromIndex == 0)
            {
                if (range.BatchId is not null)
                    throw new InvalidOperationException(
                        "النطاق بيبدأ من أول مرحلة في الخط — دي دفعة جديدة، مينفعش تتربط بدفعة واقفة");

                var fresh = new ProductionBatch
                {
                    ProductId = product.Id,
                    StartedDate = date.Date,
                    Quantity = range.PieceCount,
                    LastCompletedStageId = null,
                    Status = BatchStatus.Open
                };

                await _batchRepo.AddAsync(fresh);
                return fresh;
            }

            // ---------- رصيد افتتاحي: القطع عدّت المراحل السابقة برّه النظام ----------
            // ده المخرج الوحيد المسموح بيه من قاعدة الربط، وهو صريح ومتسجّل
            // على الدفعة — مش استثناء صامت
            if (range.IsOpeningBalance)
            {
                if (range.BatchId is not null)
                    throw new InvalidOperationException(
                        "الرصيد الافتتاحي بيفتح دفعة جديدة — مينفعش يتربط بدفعة واقفة في نفس الوقت");

                var opening = new ProductionBatch
                {
                    ProductId = product.Id,
                    StartedDate = date.Date,
                    Quantity = range.PieceCount,
                    // عدّت كل اللي قبل نقطة الدخول
                    LastCompletedStageId = line[fromIndex - 1].Id,
                    Status = BatchStatus.Open,
                    IsOpeningBalance = true,
                    Notes = $"رصيد افتتاحي: دخلت الخط عند \"{line[fromIndex].StageName}\""
                };

                await _batchRepo.AddAsync(opening);
                return opening;
            }

            // ---------- نطاق من نص الخط = لازم يكمّل دفعة ----------
            if (range.BatchId is null)
                throw new InvalidOperationException(
                    $"النطاق بيبدأ من مرحلة \"{line[fromIndex].StageName}\" مش من أول الخط.\n\n" +
                    "اختار من قايمة \"القطع دي جاية منين؟\":\n" +
                    "• دفعة واقفة — لو القطع دي بقيّة شغل مسجّل في النظام\n" +
                    $"• رصيد افتتاحي — لو القطع عدّت المراحل اللي قبل \"{line[fromIndex].StageName}\" " +
                    "قبل ما نظام الدفعات يشتغل");

            var batch = await _batchRepo.GetWithDetailsAsync(range.BatchId.Value)
                ?? throw new InvalidOperationException("الدفعة المحددة غير موجودة");

            if (batch.ProductId != product.Id)
                throw new InvalidOperationException("الدفعة المحددة تابعة لمنتج تاني");

            if (batch.Status != BatchStatus.Open)
                throw new InvalidOperationException(
                    batch.Status == BatchStatus.Completed
                        ? "الدفعة دي خلصت الخط خلاص"
                        : "الدفعة دي متلغية");

            // المرحلة اللي المفروض تكمّل منها — لازم تطابق بداية النطاق
            var expected = NextStage(batch, line)
                ?? throw new InvalidOperationException(
                    "خط إنتاج المنتج اتغيّر بعد ما الدفعة دي بدأت — راجع ترتيب المراحل");

            if (expected.Id != range.FromStageId)
                throw new InvalidOperationException(
                    $"الدفعة دي واقفة عند \"{expected.StageName}\" — " +
                    $"النطاق لازم يبدأ منها مش من \"{line[fromIndex].StageName}\"");

            if (range.PieceCount > batch.Quantity)
                throw new InvalidOperationException(
                    $"الدفعة فيها {batch.Quantity} قطعة بس — مينفعش تسجل {range.PieceCount}");

            return batch;
        }

        /// <summary>
        /// بيحرّك الدفعة لآخر مرحلة في النطاق بعد ما الإنتاج اتسجل، ويقسمها
        /// لو التكميل كان جزئي، ويقفلها لو وصلت لآخر الخط.
        /// بيرجّع ملخص الحركة للرسالة اللي بتظهر للمستخدم.
        /// </summary>
        public async Task<BatchMovementDto> AdvanceAsync(
            ProductionBatch batch, Product product, int toStageId, int pieces, DateTime date)
        {
            var line = ActiveLine(product);
            var toIndex = line.FindIndex(s => s.Id == toStageId);
            if (toIndex < 0)
                throw new InvalidOperationException("مرحلة نهاية النطاق مش موجودة في خط المنتج");

            var leftBehind = batch.Quantity - pieces;
            var wasSplit = leftBehind > 0;

            // تكميل جزئي: الباقي بيخرج في دفعة مستقلة واقفة عند نفس المرحلة
            if (wasSplit)
            {
                await _batchRepo.AddAsync(new ProductionBatch
                {
                    ProductId = batch.ProductId,
                    StartedDate = batch.StartedDate, // نفس القطع، نفس تاريخ الدخول
                    Quantity = leftBehind,
                    LastCompletedStageId = batch.LastCompletedStageId,
                    Status = BatchStatus.Open,
                    SplitFromBatchId = batch.Id
                });

                batch.Quantity = pieces;
            }

            batch.LastCompletedStageId = toStageId;

            var reachedEnd = toIndex == line.Count - 1;
            if (reachedEnd)
            {
                batch.Status = BatchStatus.Completed;
                batch.CompletedDate = date.Date;
            }

            // دفعة لسه ماتحفظتش (Id = 0) بتبقى في حالة Added — نداء Update
            // عليها بيحوّلها Modified وبيلغي الإضافة. تعديل خصايصها كفاية
            // لأن الـ ChangeTracker ماسكها أصلاً.
            if (batch.Id != 0) _batchRepo.Update(batch);

            return new BatchMovementDto
            {
                BatchId = batch.Id,
                ProductName = product.Name,
                Pieces = pieces,
                Status = batch.Status,
                WasSplit = wasSplit,
                LeftBehindPieces = leftBehind,
                StoppedAtStageName = reachedEnd ? "" : (line.ElementAtOrDefault(toIndex + 1)?.StageName ?? "")
            };
        }

        /// <summary>
        /// يلغي دفعة واقفة (هالك/خردة). القطع بتخرج من الواقف من غير ما
        /// تتحسب إنتاج تام، وسجلات إنتاج العمال عليها بتفضل زي ما هي —
        /// هما اشتغلوا فعلاً وبياخدوا أجرهم.
        /// </summary>
        public async Task CancelAsync(int batchId, string? reason)
        {
            var batch = await _batchRepo.GetByIdAsync(batchId)
                ?? throw new InvalidOperationException("الدفعة المحددة غير موجودة");

            if (batch.Status != BatchStatus.Open)
                throw new InvalidOperationException("مينفعش تلغي دفعة مش واقفة في الخط");

            batch.Status = BatchStatus.Cancelled;
            batch.Notes = reason;

            _batchRepo.Update(batch);
            await _batchRepo.SaveChangesAsync();
        }

        // ======================= مساعدات =======================

        /// <summary>مراحل المنتج النشطة بترتيب الخط — مصدر الحقيقة لكل الحسابات هنا</summary>
        public static List<ProductionStage> ActiveLine(Product product) =>
            product.Stages
                .Where(s => s.IsActive)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();

        /// <summary>
        /// المرحلة اللي الدفعة المفروض تكمّل منها. null لو آخر مرحلة عدّتها
        /// مبقتش في الخط (اتوقفت أو اتشالت بعد ما الدفعة بدأت).
        /// </summary>
        public static ProductionStage? NextStage(ProductionBatch batch, List<ProductionStage> line)
        {
            if (line.Count == 0) return null;
            if (batch.LastCompletedStageId is null) return line[0];

            var index = line.FindIndex(s => s.Id == batch.LastCompletedStageId.Value);
            if (index < 0) return null;

            return index + 1 < line.Count ? line[index + 1] : null;
        }

        private static OpenBatchDto? Describe(
            ProductionBatch batch, string productName, List<ProductionStage> line, DateTime asOf)
        {
            var next = NextStage(batch, line);
            if (next is null) return null; // خط اتغيّر — بتتعرض في شاشة الإقفال مش هنا

            var nextIndex = line.FindIndex(s => s.Id == next.Id);

            return new OpenBatchDto
            {
                BatchId = batch.Id,
                ProductId = batch.ProductId,
                ProductName = productName,
                Quantity = batch.Quantity,
                StartedDate = batch.StartedDate,
                LastCompletedStageId = batch.LastCompletedStageId,
                LastCompletedStageName = batch.LastCompletedStage?.StageName ?? "",
                NextStageId = next.Id,
                NextStageName = next.StageName,
                NextStageOrder = nextIndex + 1,
                CompletedStages = nextIndex,
                TotalStages = line.Count,
                DaysWaiting = DaysBetween(batch.StartedDate, asOf)
            };
        }

        private static int DaysBetween(DateTime from, DateTime to) =>
            Math.Max(0, (int)(to.Date - from.Date).TotalDays);
    }
}
