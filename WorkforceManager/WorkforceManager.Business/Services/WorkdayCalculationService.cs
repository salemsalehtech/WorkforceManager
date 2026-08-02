using WorkforceManager.Business.DTOs;
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
        private readonly IUnitOfWork _unitOfWork;

        public WorkdayCalculationService(
            IDailyProductionRepository productionRepo,
            IGenericRepository<ProductionStage> stageRepo,
            IWorkerRepository workerRepo,
            IProductRepository productRepo,
            WorkerAssignmentGuard assignmentGuard,
            IUnitOfWork unitOfWork)
        {
            _productionRepo = productionRepo;
            _stageRepo = stageRepo;
            _workerRepo = workerRepo;
            _productRepo = productRepo;
            _assignmentGuard = assignmentGuard;
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
        /// يسجل إنتاج مجموعة عمال على نفس المرحلة في نفس اليوم دفعة واحدة —
        /// أساس شاشة الإدخال السريع: بدل ما المدير يسجل عامل عامل، بيدخل
        /// أرقام الكل ويحفظ مرة واحدة (حفظة واحدة على قاعدة البيانات).
        /// </summary>
        /// <param name="confirmOverride">
        /// موافقة صريحة على كل تعارضات الدفعة. الاستيراد بالجملة بيستخدمه
        /// لما يكون المشغّل شاف التعارضات ووافق عليها — من غيره الدفعة
        /// كلها بترفض قبل أي كتابة (يا كله يا مفيش، زي باقي التحققات).
        /// </param>
        public async Task<int> RecordProductionBatchAsync(
            int productionStageId, DateTime date,
            IEnumerable<(int WorkerId, int PieceCount)> entries, string? notes = null,
            bool confirmOverride = false)
        {
            var stage = await _stageRepo.GetByIdAsync(productionStageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            // القطع الصفرية/السالبة بتتتخطى بصمت — معناها العامل ده مشتغلش على المرحلة دي
            var accepted = entries.Where(e => e.PieceCount > 0).ToList();
            if (accepted.Count == 0) return 0;

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            // نفس القاعدة المشتركة على الدفعة كلها قبل أي كتابة
            var requested = new List<WorkerAssignmentDto>();
            foreach (var (workerId, _) in accepted)
                requested.Add(await BuildAssignmentAsync(workerId, stage));

            var check = await _assignmentGuard.CheckAsync(date, requested);
            WorkerAssignmentGuard.EnsureAllowed(check, confirmOverride);

            foreach (var (workerId, pieceCount) in accepted)
            {
                await _productionRepo.AddAsync(new DailyProduction
                {
                    WorkerId = workerId,
                    ProductionStageId = productionStageId,
                    Date = date.Date,
                    PieceCount = pieceCount,
                    PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday, // نفس الـ Snapshot بتاع التسجيل الفردي
                    Notes = notes
                });
            }

            await _productionRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return accepted.Count;
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
        /// يحذف سجل إنتاج اتسجل بالغلط — حذف فعلي (نفس قاعدة الجزاءات:
        /// السجل الغلط ملوش قيمة تاريخية تستاهل الحفظ).
        /// </summary>
        public async Task DeleteProductionAsync(int recordId)
        {
            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج غير موجود");

            _productionRepo.Remove(record);
            await _productionRepo.SaveChangesAsync();
        }

        /// <summary>إجمالي عدد اليوميات المنجزة لعامل معين في تاريخ معين (مجموع كل المراحل التي عمل عليها)</summary>
        public async Task<decimal> GetDailyWorkdaysAsync(int workerId, DateTime date)
        {
            var records = await _productionRepo.GetByDateAsync(date);
            return records.Where(r => r.WorkerId == workerId).Sum(r => r.WorkdaysCompleted);
        }
    }
}
