using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤولة عن كل عمليات "الكتابة" على المنتجات ومراحلها: إضافة/تعديل
    /// منتج، إيقافه (Soft Delete)، وإدارة مراحله ويومياتها.
    /// نقطة مهمة جدًا: تعديل يومية مرحلة بيسري على التسجيلات الجديدة فقط —
    /// السجلات القديمة محمية بالـ Snapshot (PiecesPerWorkdayAtEntry)
    /// المتسجل وقت الإدخال، فالحسابات التاريخية عمرها ما بتتأثر.
    /// </summary>
    public class ProductManagementService
    {
        private readonly IProductRepository _productRepo;
        private readonly IGenericRepository<ProductionStage> _stageRepo;
        private readonly SoftDeleteService _softDelete;
        private readonly DeletionScopeService _scope;

        public ProductManagementService(
            IProductRepository productRepo,
            IGenericRepository<ProductionStage> stageRepo,
            SoftDeleteService softDelete,
            DeletionScopeService scope)
        {
            _productRepo = productRepo;
            _stageRepo = stageRepo;
            _softDelete = softDelete;
            _scope = scope;
        }

        // ======================= المنتجات =======================

        /// <summary>يضيف منتج جديد (الاسم إجباري)</summary>
        public async Task<Product> CreateProductAsync(string name, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم المنتج مطلوب", nameof(name));

            var product = new Product
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()
            };

            await _productRepo.AddAsync(product);
            await _productRepo.SaveChangesAsync();
            return product;
        }

        /// <summary>يعدّل بيانات منتج موجود</summary>
        public async Task<Product> UpdateProductAsync(int productId, string name, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم المنتج مطلوب", nameof(name));

            var product = await _productRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            product.Name = name.Trim();
            product.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

            _productRepo.Update(product);
            await _productRepo.SaveChangesAsync();
            return product;
        }

        /// <summary>
        /// يحدّد صورة المنتج أو يشيلها (<paramref name="imageData"/> = null).
        ///
        /// منفصلة عن <see cref="UpdateProductAsync"/> عن قصد: تعديل اسم
        /// المنتج مالوش لازمة يبعت الصورة كلها معاه في كل مرة، ولا يمسحها
        /// بالغلط لو المتصل نسي يبعتها.
        /// </summary>
        public async Task<Product> SetProductImageAsync(int productId, byte[]? imageData)
        {
            var product = await _productRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            product.ImageData = imageData is { Length: > 0 } ? imageData : null;

            _productRepo.Update(product);
            await _productRepo.SaveChangesAsync();
            return product;
        }

        /// <summary>
        /// إيقاف منتج (توقف إنتاجه): بيختفي هو ومراحله من شاشة التسجيل،
        /// وكل سجلاته التاريخية بتفضل محفوظة ومحسوبة في التقارير القديمة.
        /// </summary>
        public async Task DeactivateProductAsync(int productId)
        {
            var product = await _productRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            product.IsActive = false;
            _productRepo.Update(product);
            await _productRepo.SaveChangesAsync();
        }

        /// <summary>
        /// يشيل منتج من النظام نهائيًا — **حذف ناعم** بكلمة سر وسبب.
        ///
        /// الفرق عن الإيقاف: الإيقاف بيقول "وقفنا إنتاجه دلوقتي" وله
        /// رجوع، والحذف بيقول "المنتج ده مبقاش من المصنع".
        ///
        /// سجلات الإنتاج القديمة عليه بتفضل كلها — تقرير الشهر اللي فات
        /// لازم يفضل يقرا صح حتى بعد ما المنتج يتشال.
        /// </summary>
        public async Task<SoftDeleteResult> DeleteProductAsync(
            int productId, string operationsPassword, string reason)
        {
            var product = await _productRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            var result = await _softDelete.DeleteAsync(
                product,
                new DeletionDescriptor
                {
                    Action = SensitiveAction.DeleteProduct,
                    EventType = ActivityEventType.ProductDeleted,
                    EntityType = nameof(Product),
                    EntityId = product.Id,
                    EntityName = product.Name
                },
                operationsPassword,
                reason,
                // مفيش إنتاج على أي مرحلة من مراحله = يتمسح خالص
                // (ومراحله معاه بالـ Cascade)
                removePermanently: await _scope.CanRemoveProductAsync(product.Id)
                    ? () => _productRepo.Remove(product)
                    : null);

            // نفس قاعدة العامل: الإخفاء من القوايم بالعلامة اللي كل
            // الشاشات بتحترمها أصلاً، لأن الفلتر العام مش على المنتج
            // (عشان التقارير التاريخية تفضل شغالة)
            if (result.IsDeleted && !result.WasPermanent && product.IsActive)
            {
                product.IsActive = false;
                _productRepo.Update(product);
                await _productRepo.SaveChangesAsync();
            }

            return result;
        }

        /// <summary>إعادة تفعيل منتج موقوف (رجع للإنتاج تاني)</summary>
        public async Task ReactivateProductAsync(int productId)
        {
            var product = await _productRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            product.IsActive = true;
            _productRepo.Update(product);
            await _productRepo.SaveChangesAsync();
        }

        // ======================= المراحل =======================

        /// <summary>
        /// يضيف مرحلة جديدة لمنتج بيوميتها. اسم المرحلة ميتكررش
        /// جوه نفس المنتج (لكن عادي يتكرر في منتجات تانية بيومية مختلفة —
        /// دي قاعدة أساسية في النظام). لو الترتيب مش متحدد بياخد آخر ترتيب + 1.
        /// </summary>
        public async Task<ProductionStage> AddStageAsync(
            int productId, string stageName, int piecesPerWorkday, int? sortOrder = null)
        {
            if (string.IsNullOrWhiteSpace(stageName))
                throw new ArgumentException("اسم المرحلة مطلوب", nameof(stageName));
            if (piecesPerWorkday <= 0)
                throw new ArgumentException("اليومية يجب أن تكون رقمًا موجبًا أكبر من صفر", nameof(piecesPerWorkday));

            var product = await _productRepo.GetWithStagesAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            var trimmedName = stageName.Trim();
            if (product.Stages.Any(s => s.StageName == trimmedName))
                throw new InvalidOperationException($"المرحلة \"{trimmedName}\" موجودة بالفعل في هذا المنتج");

            var stage = new ProductionStage
            {
                ProductId = productId,
                StageName = trimmedName,
                PiecesPerWorkday = piecesPerWorkday,
                SortOrder = sortOrder ?? (product.Stages.Count == 0 ? 1 : product.Stages.Max(s => s.SortOrder) + 1)
            };

            await _stageRepo.AddAsync(stage);
            await _stageRepo.SaveChangesAsync();
            return stage;
        }

        /// <summary>
        /// يعدّل مرحلة (الاسم / اليومية / الترتيب). تغيير اليومية بيسري على
        /// التسجيلات الجديدة فقط — القديم محمي بالـ Snapshot.
        /// </summary>
        public async Task<ProductionStage> UpdateStageAsync(
            int stageId, string stageName, int piecesPerWorkday, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(stageName))
                throw new ArgumentException("اسم المرحلة مطلوب", nameof(stageName));
            if (piecesPerWorkday <= 0)
                throw new ArgumentException("اليومية يجب أن تكون رقمًا موجبًا أكبر من صفر", nameof(piecesPerWorkday));

            var stage = await _stageRepo.GetByIdAsync(stageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            // منع تكرار الاسم الجديد مع مرحلة تانية في نفس المنتج
            var trimmedName = stageName.Trim();
            var duplicate = (await _stageRepo.FindAsync(
                s => s.ProductId == stage.ProductId && s.Id != stageId && s.StageName == trimmedName))
                .Any();
            if (duplicate)
                throw new InvalidOperationException($"المرحلة \"{trimmedName}\" موجودة بالفعل في هذا المنتج");

            stage.StageName = trimmedName;
            stage.PiecesPerWorkday = piecesPerWorkday;
            stage.SortOrder = sortOrder;

            _stageRepo.Update(stage);
            await _stageRepo.SaveChangesAsync();
            return stage;
        }

        /// <summary>
        /// يحرّك مرحلة خطوة لفوق أو لتحت في خط الإنتاج (بيبدّل ترتيبها مع
        /// جارتها). الترتيب مش شكلي: نطاقات الإنتاج ("من مرحلة كذا لمرحلة
        /// كذا") بتتحسب بترتيب المراحل، فالترتيب الغلط بيغيّر معنى النطاق.
        ///
        /// بيرجّع false لو المرحلة أصلاً في أول أو آخر الخط (مفيش حركة ممكنة).
        /// </summary>
        public async Task<bool> MoveStageAsync(int stageId, bool moveUp)
        {
            var stage = await _stageRepo.GetByIdAsync(stageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            // كل مراحل المنتج بترتيب الخط (نفس ترتيب شاشة التسجيل بالظبط)
            var siblings = (await _stageRepo.FindAsync(s => s.ProductId == stage.ProductId))
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();

            var index = siblings.FindIndex(s => s.Id == stageId);
            var targetIndex = moveUp ? index - 1 : index + 1;
            if (targetIndex < 0 || targetIndex >= siblings.Count) return false;

            // إعادة ترقيم الكل من 1 بعد التبديل — بيصلّح أي ترتيب متكرر أو
            // فيه فجوات جاي من بيانات قديمة، وبيضمن ترتيب نظيف دايمًا
            (siblings[index], siblings[targetIndex]) = (siblings[targetIndex], siblings[index]);
            for (var i = 0; i < siblings.Count; i++)
            {
                siblings[i].SortOrder = i + 1;
                _stageRepo.Update(siblings[i]);
            }

            await _stageRepo.SaveChangesAsync();
            return true;
        }

        /// <summary>إيقاف مرحلة (بتختفي من شاشة التسجيل، وسجلاتها التاريخية محفوظة)</summary>
        public async Task DeactivateStageAsync(int stageId)
        {
            var stage = await _stageRepo.GetByIdAsync(stageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            stage.IsActive = false;
            _stageRepo.Update(stage);
            await _stageRepo.SaveChangesAsync();
        }

        /// <summary>
        /// يشيل مرحلة من الخط نهائيًا — **حذف ناعم** بكلمة سر وسبب.
        ///
        /// سجلات الإنتاج على المرحلة دي بتفضل، وأجور العمال اللي اشتغلوا
        /// عليها متأثرش — اليومية محفوظة كـ Snapshot على كل سجل أصلاً.
        /// </summary>
        public async Task<SoftDeleteResult> DeleteStageAsync(
            int stageId, string operationsPassword, string reason)
        {
            var stage = await _stageRepo.GetByIdAsync(stageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            var result = await _softDelete.DeleteAsync(
                stage,
                new DeletionDescriptor
                {
                    Action = SensitiveAction.DeleteStage,
                    EventType = ActivityEventType.StageDeleted,
                    EntityType = nameof(ProductionStage),
                    EntityId = stage.Id,
                    EntityName = stage.StageName,
                    Details = $"اليومية وقت الحذف: {stage.PiecesPerWorkday} قطعة"
                },
                operationsPassword,
                reason,
                // مفيش إنتاج اتسجّل عليها = تتمسح خالص (ومهارات العمال
                // عليها بتروح معاها بالـ Cascade — مهارة على مرحلة
                // مامعدش ليها وجود مالهاش أي معنى)
                removePermanently: await _scope.CanRemoveStageAsync(stage.Id)
                    ? () => _stageRepo.Remove(stage)
                    : null);

            if (result.IsDeleted && !result.WasPermanent && stage.IsActive)
            {
                stage.IsActive = false;
                _stageRepo.Update(stage);
                await _stageRepo.SaveChangesAsync();
            }

            return result;
        }

        /// <summary>إعادة تفعيل مرحلة موقوفة</summary>
        public async Task ReactivateStageAsync(int stageId)
        {
            var stage = await _stageRepo.GetByIdAsync(stageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            stage.IsActive = true;
            _stageRepo.Update(stage);
            await _stageRepo.SaveChangesAsync();
        }
    }
}
