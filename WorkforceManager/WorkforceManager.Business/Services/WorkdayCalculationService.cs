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
        private readonly OperationsPasswordService _gate;
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IUnitOfWork _unitOfWork;

        public WorkdayCalculationService(
            IDailyProductionRepository productionRepo,
            IGenericRepository<ProductionStage> stageRepo,
            IWorkerRepository workerRepo,
            IProductRepository productRepo,
            WorkerAssignmentGuard assignmentGuard,
            SoftDeleteService softDelete,
            OperationsPasswordService gate,
            IProductionDayClosureRepository closureRepo,
            IUnitOfWork unitOfWork)
        {
            _productionRepo = productionRepo;
            _stageRepo = stageRepo;
            _workerRepo = workerRepo;
            _productRepo = productRepo;
            _assignmentGuard = assignmentGuard;
            _softDelete = softDelete;
            _gate = gate;
            _closureRepo = closureRepo;
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// يرفض الكتابة على يوم مقفول.
        ///
        /// كان الفحص ده في <see cref="ProductionFlowService"/> بس، يعني
        /// القفل كان بيتلفّ حواليه من المسار ده: تسجيل سجل واحد أو تعديل
        /// عدد قطع سجل محفوظ كانوا بيعدّوا على يوم مقفول عادي — والمستخدم
        /// يكون شاف الأرقام ووافق عليها وطبع تقرير، والأرقام تتغيّر بعديها.
        ///
        /// الحذف **مستثنى** عن قصد: حذف بكلمة سر وسبب مكتوب هو الطريق
        /// المقصود لتصحيح يوم اتقفل بالغلط.
        /// </summary>
        private async Task EnsureDayIsOpenAsync(DateTime date)
        {
            if (await _closureRepo.IsClosedAsync(date))
                throw new InvalidOperationException(DayClosureService.ClosedDayMessage(date));
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
            bool confirmOverride = false)
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
                PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday // Snapshot اليومية وقت التسجيل
            };

            // نفس قاعدة رحلة الإنتاج بالظبط: تحقق وكتابة جوه معاملة واحدة.
            // فحص القفل جوه المعاملة عشان يقرا تحت نفس قفل الكتابة اللي
            // الإدخال بيتم تحته — من برّا كان ممكن يوم يتقفل بين الفحص
            // والكتابة فيعدّي سجل على يوم مقفول
            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            await EnsureDayIsOpenAsync(date);

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
        public async Task<DailyProduction> UpdateProductionAsync(
            int recordId, int newPieceCount, string operationsPassword = "")
        {
            if (newPieceCount <= 0)
                throw new ArgumentException("عدد القطع يجب أن يكون أكبر من صفر", nameof(newPieceCount));

            // تصحيح القطع بيعيد حساب اليومية، واليومية هي الأجر. النوع
            // ده كان معرّف في SensitiveAction من زمان (EditProductionPieces)
            // ومحدش استخدمه — فتعديل رقم إنتاج محفوظ كان بيعدّي من غير
            // كلمة سر بينما حذفه بيتطلبها
            var gate = await _gate.VerifyAsync(SensitiveAction.EditProductionPieces, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج غير موجود");

            // تعديل رقم على يوم مقفول = تغيير أرقام المستخدم شافها ووافق
            // عليها وممكن يكون طبعها
            await EnsureDayIsOpenAsync(record.Date);

            record.PieceCount = newPieceCount;
            _productionRepo.Update(record);
            await _productionRepo.SaveChangesAsync();
            return record;
        }

        /// <summary>
        /// يشيل سجل إنتاج اتسجل بالغلط — بكلمة سر وسبب مكتوب.
        ///
        /// السؤال "الأجر نقص ليه الأسبوع ده؟" لازم تبقى ليه إجابة، وهي
        /// في سجل العمليات: مين شال السجل وإمتى وليه وكام قطعة كانت
        /// عليه. الإجابة دي مش محتاجة الصف نفسه يفضل قاعد في الجدول.
        ///
        /// **بيتمسح من الجدول خالص.** مفيش أي مفتاح أجنبي بيشاور على
        /// سجل الإنتاج، فمفيش حاجة بتتكسر بمسحه — وكان بيتعلّم محذوف
        /// ويفضل قاعد في الجدول للأبد، كل استعلام بيعدّي عليه وهو
        /// مستبعد بفلتر. اللي بيفضل هو حدث السجل: مين مسحه، إمتى،
        /// وليه، وكام قطعة كانت عليه.
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
                reason,
                removePermanently: () => _productionRepo.Remove(record));
        }

        /// <summary>
        /// يشيل **كل** سجلات إنتاج يوم واحد — حذف ناعم بكلمة سر وسبب.
        ///
        /// موجودة كعملية واحدة مش حلقة على
        /// <see cref="DeleteProductionAsync"/> لسببين:
        ///   • كلمة السر بتتسأل مرة واحدة، مش مرة لكل سجل
        ///   • كل السجلات بتتشال في معاملة واحدة — يا كلها يا ولا واحد.
        ///     الحلقة كانت ممكن تسيب نص يوم متشال ونص موجود لو حصل خطأ
        ///     في النص، وده أسوأ من إن الحذف كله يفشل
        ///
        /// اليوم المقفول بيتشال عادي: القفل بيمنع **تسجيل** جديد، والحذف
        /// بكلمة سر وسبب هو الطريق المقصود لتصحيح يوم اتقفل بالغلط.
        /// </summary>
        public async Task<SoftDeleteResult> DeleteProductionDayAsync(
            DateTime date, string operationsPassword, string reason)
        {
            var records = await _productionRepo.GetByDateAsync(date);
            if (records.Count == 0)
                return SoftDeleteResult.Fail($"مفيش أي إنتاج مسجّل يوم {date:yyyy/MM/dd}");

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var totalPieces = records.Sum(r => r.PieceCount);
            var wasNotConfigured = false;

            foreach (var record in records)
            {
                var result = await _softDelete.DeleteAsync(
                    record,
                    new DeletionDescriptor
                    {
                        Action = SensitiveAction.DeleteProduction,
                        EventType = ActivityEventType.ProductionRecordDeleted,
                        EntityType = nameof(DailyProduction),
                        EntityId = record.Id,
                        EntityName = $"سجل إنتاج #{record.Id} — {record.PieceCount} قطعة",
                        Details = $"ضمن حذف يوم {date:yyyy/MM/dd} كامل " +
                                  $"({records.Count} سجل، {totalPieces} قطعة)"
                    },
                    operationsPassword,
                    reason,
                    // الحفظ مؤجّل لآخر السجل: السجلات كلها بتنزل مع بعض
                    saveChanges: false,
                    removePermanently: () => _productionRepo.Remove(record));

                // أول رفض بيوقف كل حاجة — المعاملة بتتلغي عند الخروج
                // من غير Commit، فمفيش سجل واحد اتشال
                if (!result.IsDeleted) return result;

                wasNotConfigured = result.PasswordNotConfigured;
            }

            await _productionRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return SoftDeleteResult.Success(wasNotConfigured);
        }
    }
}
