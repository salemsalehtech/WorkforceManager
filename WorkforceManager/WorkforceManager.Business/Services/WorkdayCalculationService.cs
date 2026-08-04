using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// مسؤول عن عملية واحدة بس وبيعملها صح: تسجيل قطع منتجة لعامل
    /// في مرحلة معينة، وحساب عدد "اليوميات" الناتجة عنها تلقائيًا.
    /// أي منطق حسابي متعلق باليوميات لازم يمر من هنا، مش يتكتب في
    /// الواجهة (UI) مباشرة.
    /// </summary>
    public class WorkdayCalculationService
    {
        private readonly IDailyProductionRepository _productionRepo;
        private readonly IGenericRepository<ProductionStage> _stageRepo;
        private readonly IWorkerRepository _workerRepo;
        private readonly IProductRepository _productRepo;
        private readonly WorkerAssignmentGuard _assignmentGuard;
        private readonly SoftDeleteService _softDelete;
        private readonly IUnitOfWork _unitOfWork;

        public WorkdayCalculationService(
            IDailyProductionRepository productionRepo,
            IGenericRepository<ProductionStage> stageRepo,
            IWorkerRepository workerRepo,
            IProductRepository productRepo,
            WorkerAssignmentGuard assignmentGuard,
            SoftDeleteService softDelete,
            IUnitOfWork unitOfWork)
        {
            _productionRepo = productionRepo;
            _stageRepo = stageRepo;
            _workerRepo = workerRepo;
            _productRepo = productRepo;
            _assignmentGuard = assignmentGuard;
            _softDelete = softDelete;
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// يبني وصف التكليف اللي القاعدة المشتركة بتشتغل عليه. الأسماء
        /// هنا للرسائل بس — المقارنة نفسها بتتم بالمعرّفات.
        /// </summary>
        private async Task<WorkerAssignmentDto> BuildAssignmentAsync(int workerId, ProductionStage stage)
        {
            var worker = await _workerRepo.GetByIdAsync(workerId);
            var product = await _productRepo.GetByIdAsync(stage.ProductId);

            return new WorkerAssignmentDto
            {
                WorkerId = workerId,
                WorkerName = worker?.FullName ?? string.Empty,
                ProductId = stage.ProductId,
                ProductName = product?.Name ?? string.Empty,
                ProductionStageId = stage.Id,
                StageName = stage.StageName
            };
        }

        /// <summary>
        /// يسجل إنتاج عامل في مرحلة معينة، ويحسب عدد اليوميات المنجزة
        /// تلقائيًا بناءً على اليومية الحالية للمرحلة، مع حفظ
        /// نسخة (Snapshot) من اليومية وقت التسجيل حماية للسجل من أي
        /// تعديل لاحق لليومية.
        /// </summary>
        /// <param name="confirmOverride">
        /// موافقة صريحة على تكليف العامل بمنتج/مرحلة تانية في نفس اليوم —
        /// نفس معنى المعامل في <see cref="ProductionFlowService.RecordFlowAsync"/>
        /// (القاعدة واحدة في <see cref="WorkerAssignmentGuard"/> للاتنين).
        /// </param>
        public async Task<DailyProduction> RecordProductionAsync(
            int workerId, int productionStageId, int pieceCount, DateTime date,
            string? notes = null, bool confirmOverride = false)
        {
            if (pieceCount <= 0)
                throw new ArgumentException("عدد القطع يجب أن يكون أكبر من صفر", nameof(pieceCount));

            var stage = await _stageRepo.GetByIdAsync(productionStageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            var record = new DailyProduction
            {
                WorkerId = workerId,
                ProductionStageId = productionStageId,
                Date = date.Date,
                PieceCount = pieceCount,
                PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday, // Snapshot اليومية وقت التسجيل
                Notes = notes
            };

            // نفس قاعدة رحلة الإنتاج بالظبط: تحقق وكتابة جوه معاملة واحدة
            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var check = await _assignmentGuard.CheckAsync(
                date, new[] { await BuildAssignmentAsync(workerId, stage) });
            WorkerAssignmentGuard.EnsureAllowed(check, confirmOverride);

            await _productionRepo.AddAsync(record);
            await _productionRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return record;
        }

        /// <summary>
        /// يصحّح عدد قطع سجل إنتاج اتحفظ بالغلط. اليومية المحفوظة وقت
        /// التسجيل (Snapshot) بتفضل زي ما هي — التصحيح للقطع بس،
        /// واليوميات بتتعاد حسابها تلقائيًا (خاصية محسوبة).
        /// </summary>
        public async Task<DailyProduction> UpdateProductionAsync(int recordId, int newPieceCount)
        {
            if (newPieceCount <= 0)
                throw new ArgumentException("عدد القطع يجب أن يكون أكبر من صفر", nameof(newPieceCount));

            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج غير موجود");

            record.PieceCount = newPieceCount;
            _productionRepo.Update(record);
            await _productionRepo.SaveChangesAsync();
            return record;
        }

        /// <summary>
        /// يشيل سجل إنتاج اتسجل بالغلط — **حذف ناعم** بكلمة سر وسبب.
        ///
        /// كان حذف فعلي قبل كده. اتغيّر لأن السجل ده أساس أجر عامل: لو
        /// اتشال فعليًا، السؤال "الأجر نقص ليه الأسبوع ده؟" مبقاش ليه
        /// إجابة في أي مكان — ولا حتى مين شاله ولا إمتى.
        ///
        /// السجل المشال بيختفي من كل الحسابات (فلتر عام على
        /// DailyProduction) بس بيفضل موجود للمراجعة.
        /// </summary>
        public async Task<SoftDeleteResult> DeleteProductionAsync(
            int recordId, string operationsPassword, string reason)
        {
            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج غير موجود");

            var stage = await _stageRepo.GetByIdAsync(record.ProductionStageId);
            var label = stage is null
                ? $"سجل إنتاج #{record.Id}"
                : $"{stage.StageName} — {record.PieceCount} قطعة";

            return await _softDelete.DeleteAsync(
                record,
                new DeletionDescriptor
                {
                    Action = SensitiveAction.DeleteProduction,
                    EventType = ActivityEventType.ProductionRecordDeleted,
                    EntityType = nameof(DailyProduction),
                    EntityId = record.Id,
                    EntityName = label,
                    Details = $"يوم {record.Date:yyyy/MM/dd} — {record.PieceCount} قطعة"
                },
                operationsPassword,
                reason);
        }
    }
}
