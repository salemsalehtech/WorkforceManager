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
        private readonly ActivityLogService _log;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly ProductionStageOutputService _productionOutput;
        private readonly IWorkerSkillRepository _workerSkillRepo;
        private readonly IGenericRepository<InitialBalanceUsage> _initialBalanceUsages;
        private readonly ProductionFlowService _productionFlow;

        public WorkdayCalculationService(
            IDailyProductionRepository productionRepo,
            IGenericRepository<ProductionStage> stageRepo,
            IWorkerRepository workerRepo,
            IProductRepository productRepo,
            WorkerAssignmentGuard assignmentGuard,
            SoftDeleteService softDelete,
            OperationsPasswordService gate,
            IProductionDayClosureRepository closureRepo,
            IUnitOfWork unitOfWork,
            ActivityLogService log,
            IAttendanceRepository attendanceRepo,
            IHourlyWorkLogRepository hourlyRepo,
            ProductionStageOutputService productionOutput,
            IWorkerSkillRepository workerSkillRepo,
            IGenericRepository<InitialBalanceUsage> initialBalanceUsages,
            ProductionFlowService productionFlow)
        {
            _log = log;
            _productionRepo = productionRepo;
            _stageRepo = stageRepo;
            _workerRepo = workerRepo;
            _productRepo = productRepo;
            _assignmentGuard = assignmentGuard;
            _softDelete = softDelete;
            _gate = gate;
            _closureRepo = closureRepo;
            _unitOfWork = unitOfWork;
            _attendanceRepo = attendanceRepo;
            _hourlyRepo = hourlyRepo;
            _productionOutput = productionOutput;
            _workerSkillRepo = workerSkillRepo;
            _initialBalanceUsages = initialBalanceUsages;
            _productionFlow = productionFlow;
        }

        /// <summary>
        /// سجل الحضور "حاضر" التلقائي بيتولد بس لما فيه إنتاج (أو شغل
        /// بالساعة) مسجّل لعامل في يوم — لو الإنتاج اتشال (سجل واحد أو
        /// اليوم كله) وفضل العامل من غير أي حاجة تانية تبرر حضوره في
        /// اليوم ده، سجل الحضور نفسه بيتشال معاه بدل ما يفضل "حاضر"
        /// بلا أي سبب. لو عايز يتسجّل حاضر فعلاً بعد كده، بيتحط بإيده
        /// من شاشة الحضور زي أي يوم عادي — "مفيش سجل" حالة صحيحة
        /// ومقصودة، مش خطأ.
        /// </summary>
        private async Task CleanupOrphanedAttendanceAsync(int workerId, DateTime date)
        {
            var stillHasProduction = (await _productionRepo.GetByDateAsync(date)).Any(r => r.WorkerId == workerId);
            if (stillHasProduction) return;

            if (await _hourlyRepo.GetByWorkerAndDateAsync(workerId, date) is not null) return;

            var attendance = await _attendanceRepo.GetByWorkerAndDateAsync(workerId, date);
            if (attendance is not null) _attendanceRepo.Remove(attendance);
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
        /// يصحّح عدد قطع سجل إنتاج اتحفظ بالغلط، واختياريًا بينقل السجل
        /// بالكامل لعامل تاني (<paramref name="newWorkerId"/>) — لحالة
        /// "اتسجّل على عامل غلط بالغلط": يومية السجل ده بتتشال من
        /// حساب العامل القديم وتتحط على الجديد، مش تعديل رقم بس.
        ///
        /// اليومية المحفوظة وقت التسجيل (Snapshot) بتفضل زي ما هي —
        /// التصحيح للقطع والعامل بس، واليوميات بتتعاد حسابها تلقائيًا
        /// (خاصية محسوبة).
        /// </summary>
        /// <param name="newWorkerId">
        /// null أو نفس عامل السجل الحالي = مفيش تغيير عامل، تصحيح قطع
        /// عادي. عامل مختلف = نقل السجل بالكامل له، بعد التحقق من
        /// تأهيله على نفس المرحلة (نفس شرط شاشة التسجيل العادية) ومن
        /// نفس قاعدة تعارض التكليف (<see cref="WorkerAssignmentGuard"/>).
        /// </param>
        /// <param name="confirmOverride">
        /// موافقة صريحة على تكليف العامل الجديد بمنتج/مرحلة تانية في
        /// نفس اليوم — بتتفحص بس لما العامل فعلاً بيتغيّر.
        /// </param>
        /// <param name="reason">سبب اختياري لتغيير العامل، بيتسجل في سجل العمليات</param>
        public async Task<DailyProduction> UpdateProductionAsync(
            int recordId, int newPieceCount, string operationsPassword = "",
            int? newWorkerId = null, bool confirmOverride = false, string? reason = null)
        {
            if (newPieceCount <= 0)
                throw new ArgumentException("عدد القطع يجب أن يكون أكبر من صفر", nameof(newPieceCount));

            // تصحيح القطع (أو تحويل السجل لعامل تاني) بيعيد حساب
            // اليومية، واليومية هي الأجر. النوع ده كان معرّف في
            // SensitiveAction من زمان (EditProductionPieces) ومحدش
            // استخدمه — فتعديل رقم إنتاج محفوظ كان بيعدّي من غير كلمة
            // سر بينما حذفه بيتطلبها
            var gate = await _gate.VerifyAsync(SensitiveAction.EditProductionPieces, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج غير موجود");

            // تعديل رقم/عامل على يوم مقفول = تغيير أرقام المستخدم شافها
            // ووافق عليها وممكن يكون طبعها
            await EnsureDayIsOpenAsync(record.Date);

            var oldPieceCount = record.PieceCount;
            var oldWorkerId = record.WorkerId;
            var isWorkerChanged = newWorkerId is not null && newWorkerId.Value != oldWorkerId;

            if (!isWorkerChanged)
            {
                // ملحوظة: تصحيح قطعة عامل هنا **ما بيلمسش** الإنتاج الفعلي
                // (ProductionStageOutput) عن قصد — رقم مستقل تمامًا (رقم النطاق
                // وقت التسجيل)، مش مشتق من قطعة عامل بعينه. شوف تعليق
                // ProductionStageOutputService.RemoveIfNowOrphanedAsync.
                record.PieceCount = newPieceCount;
                _productionRepo.Update(record);
                await _productionRepo.SaveChangesAsync();

                await _log.LogAsync(
                    ActivityEventType.ProductionPiecesEdited, "DailyProduction", record.Id,
                    entityName: record.Worker?.FullName,
                    details: $"من {oldPieceCount:N0} إلى {newPieceCount:N0} قطعة يوم {record.Date:yyyy/MM/dd}");

                return record;
            }

            // ---------- تغيير العامل: نقل يومية السجل من عامل لعامل ----------

            var stage = await _stageRepo.GetByIdAsync(record.ProductionStageId)
                ?? throw new InvalidOperationException("المرحلة المحددة غير موجودة");

            // العامل الجديد لازم يكون من العمال المؤهلين على المرحلة دي
            // فعلاً — نفس شرط شاشة التسجيل العادية، مش أي عامل عشوائي
            _ = await _workerSkillRepo.GetAsync(newWorkerId!.Value, record.ProductionStageId)
                ?? throw new InvalidOperationException("العامل الجديد مش من العمال المؤهلين على هذه المرحلة");

            var newWorker = await _workerRepo.GetByIdAsync(newWorkerId.Value)
                ?? throw new InvalidOperationException("العامل الجديد غير موجود");

            var oldWorker = await _workerRepo.GetByIdAsync(oldWorkerId);
            var product = await _productRepo.GetByIdAsync(stage.ProductId);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            // إعادة فحص القفل جوه المعاملة — نفس مبدأ RecordProductionAsync:
            // القرار لازم ياخد على بيانات محمية بقفل الكتابة
            await EnsureDayIsOpenAsync(record.Date);

            // حالة النقل الفعلي لسجل موجود بين العمال/المراحل: التعارضات
            // بتتجاهلها لأنها جزء من العملية نفسها، لأن الهدف هو "نقل شغل
            // من عامل لآخر" مش "إضافة سجل جديد". القاعدة الأساسية لعدم
            // التكرار/التعطيل ما تزال مطبقة على تسجيلات جديدة، لكن في مسار
            // التصحيح هذا النقل نفسه هو الإجراء المصرح به.
            // لا نعيد فحص WorkerAssignmentGuard هنا، لأن المسار ده يخص
            // إعادة توجيه سجل موجود فعليًا لا تسجيل تكليف جديد.

            record.PieceCount = newPieceCount;
            record.WorkerId = newWorkerId.Value;
            _productionRepo.Update(record);
            await _productionRepo.SaveChangesAsync();

            // العامل القديم: لو بقى من غير أي إنتاج أو شغل بالساعة تاني
            // نفس اليوم، حضوره التلقائي بيتشال معاه — نفس قاعدة حذف سجل
            // الإنتاج بالظبط
            await CleanupOrphanedAttendanceAsync(oldWorkerId, record.Date);

            // العامل الجديد: بياخد حضور تلقائي لليوم ده لو مالوش سجل
            // حضور أصلاً — نفس قاعدة رحلة الإنتاج العادية
            if (await _attendanceRepo.GetByWorkerAndDateAsync(newWorkerId.Value, record.Date) is null)
            {
                await _attendanceRepo.AddAsync(new Attendance
                {
                    WorkerId = newWorkerId.Value,
                    Date = record.Date,
                    Status = AttendanceStatus.Present
                });
            }

            await _productionRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            var pieceChangeNote = oldPieceCount == newPieceCount
                ? ""
                : $" ({oldPieceCount:N0} → {newPieceCount:N0} قطعة)";

            await _log.LogAsync(
                ActivityEventType.ProductionWorkerReassigned, "DailyProduction", record.Id,
                entityName: $"{oldWorker?.FullName ?? "؟"} ← {newWorker.FullName}",
                reason: reason,
                details: $"{product?.Name} / {stage.StageName} يوم {record.Date:yyyy/MM/dd}{pieceChangeNote}");

            return record;
        }

        /// <summary>
        /// تراجع عن آخر تصحيح قطع/نقل عامل على سجل — بيرجّع السجل بالظبط
        /// للحالة اللي كانت قبل التعديل مباشرة (زرار "تراجع" أو Ctrl+Z
        /// في تبويب سجلات اليوم).
        ///
        /// **من غير كلمة سر عن قصد**: المستخدم أصلاً اتحقق منه لما عمل
        /// التعديل الأصلي، والتراجع مجرد رجوع لحالة معروفة كانت محفوظة
        /// فعلاً، مش تغيير جديد — لازم يبقى سريع زي أي "تراجع" عادي.
        /// </summary>
        public async Task<DailyProduction> UndoEditAsync(
            int recordId, int previousWorkerId, int previousPieceCount)
        {
            var record = await _productionRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الإنتاج مش موجود دلوقتي — يمكن اتحذف بعد كده");

            await EnsureDayIsOpenAsync(record.Date);

            var currentWorkerId = record.WorkerId;
            var currentPieceCount = record.PieceCount;
            var workerIsChanging = previousWorkerId != currentWorkerId;

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();
            await EnsureDayIsOpenAsync(record.Date);

            record.WorkerId = previousWorkerId;
            record.PieceCount = previousPieceCount;
            _productionRepo.Update(record);
            await _productionRepo.SaveChangesAsync();

            if (workerIsChanging)
            {
                await CleanupOrphanedAttendanceAsync(currentWorkerId, record.Date);

                if (await _attendanceRepo.GetByWorkerAndDateAsync(previousWorkerId, record.Date) is null)
                {
                    await _attendanceRepo.AddAsync(new Attendance
                    {
                        WorkerId = previousWorkerId,
                        Date = record.Date,
                        Status = AttendanceStatus.Present
                    });
                }

                await _productionRepo.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            var stage = await _stageRepo.GetByIdAsync(record.ProductionStageId);
            var restoredWorker = await _workerRepo.GetByIdAsync(previousWorkerId);
            var replacedWorker = workerIsChanging ? await _workerRepo.GetByIdAsync(currentWorkerId) : null;

            await _log.LogAsync(
                ActivityEventType.ProductionRecordUndone, "DailyProduction", record.Id,
                entityName: restoredWorker?.FullName,
                details: workerIsChanging
                    ? $"تراجع عن نقل سجل: رجع لـ {restoredWorker?.FullName ?? "؟"} بدل {replacedWorker?.FullName ?? "؟"} — {stage?.StageName} يوم {record.Date:yyyy/MM/dd}"
                    : $"تراجع عن تصحيح قطع: من {currentPieceCount:N0} رجع لـ {previousPieceCount:N0} — {stage?.StageName} يوم {record.Date:yyyy/MM/dd}");

            return record;
        }

        /// <summary>
        /// تراجع عن حذف سجل إنتاج — بيعيد إنشاء سجل جديد بنفس بيانات
        /// السجل المحذوف بالظبط (عامل، مرحلة، تاريخ، قطع، واليومية
        /// المحفوظة وقت التسجيل الأصلي). الـ Id مش هيكون نفسه القديم
        /// (اتمسح خالص من الجدول)، بس مفيش أي مفتاح أجنبي بيشاور على
        /// سجل الإنتاج فمفيش فرق عمليًا.
        /// </summary>
        public async Task<DailyProduction> UndoDeleteAsync(
            int workerId, int productionStageId, DateTime date,
            int pieceCount, int piecesPerWorkdayAtEntry, bool isRework)
        {
            var stage = await _stageRepo.GetByIdAsync(productionStageId)
                ?? throw new InvalidOperationException("المرحلة اتحذفت — مش هينفع يترجع السجل");

            await EnsureDayIsOpenAsync(date);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();
            await EnsureDayIsOpenAsync(date);

            var record = new DailyProduction
            {
                WorkerId = workerId,
                ProductionStageId = productionStageId,
                Date = date.Date,
                PieceCount = pieceCount,
                PiecesPerWorkdayAtEntry = piecesPerWorkdayAtEntry,
                IsRework = isRework
            };

            await _productionRepo.AddAsync(record);
            await _productionRepo.SaveChangesAsync();

            if (await _attendanceRepo.GetByWorkerAndDateAsync(workerId, date) is null)
            {
                await _attendanceRepo.AddAsync(new Attendance
                {
                    WorkerId = workerId,
                    Date = date.Date,
                    Status = AttendanceStatus.Present
                });
                await _productionRepo.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            var worker = await _workerRepo.GetByIdAsync(workerId);
            await _log.LogAsync(
                ActivityEventType.ProductionRecordUndone, "DailyProduction", record.Id,
                entityName: worker?.FullName,
                details: $"تراجع عن حذف سجل: رجع {pieceCount:N0} قطعة — {stage.StageName} يوم {date:yyyy/MM/dd}");

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
        /// سجل الإنتاج العادي، فمفيش حاجة بتتكسر بمسحه — وكان بيتعلّم
        /// محذوف ويفضل قاعد في الجدول للأبد، كل استعلام بيعدّي عليه وهو
        /// مستبعد بفلتر. اللي بيفضل هو حدث السجل: مين مسحه، إمتى،
        /// وليه، وكام قطعة كانت عليه.
        ///
        /// **الاستثناء الوحيد**: سجل ناتج من إكمال رصيد أولي
        /// (<see cref="DailyProduction.IsBalanceCompletion"/>) ليه
        /// <see cref="InitialBalanceUsage"/> مرتبط بـ FK صارم (Restrict)
        /// — لازم يتشال هو الأول، وإلا الحذف بيفشل برسالة قاعدة بيانات
        /// خام (SQLite FK constraint) بدل ما ينفّذ. حذف الاستخدام هنا
        /// بيرجّع القطع تلقائيًا لرصيدها (RemainingQuantity محسوبة من
        /// مجموع الاستخدامات).
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

            var workerId = record.WorkerId;
            var date = record.Date;
            var stageId = record.ProductionStageId;
            var linkedUsage = await FindLinkedInitialBalanceUsageAsync(record.Id);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var result = await _softDelete.DeleteAsync(
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
                saveChanges: false,
                removePermanently: () =>
                {
                    if (linkedUsage is not null) _initialBalanceUsages.Remove(linkedUsage);
                    _productionRepo.Remove(record);
                });

            if (!result.IsDeleted) return result; // المعاملة بتتلغي من غير Commit

            // لازم يتحفظ الحذف الأول عشان الفحوصات اللي بعده (هنا وفي
            // CleanupOrphanedAttendanceAsync) تقرا من قاعدة البيانات
            // فعليًا — لو اتنادت قبل الحفظ ده، السجل هيفضل شايفينه موجود
            await _productionRepo.SaveChangesAsync();

            // لو ده كان آخر سجل باقي لنفس المرحلة/اليوم، الإنتاج الفعلي
            // المرتبط بيهم (لو موجود) بقى شبح — يتشال معاه
            await _productionOutput.RemoveIfNowOrphanedAsync(stageId, date);

            // لازم يتحفظ قبل ReconcileAutoBalancesAsync — RemoveIfNowOrphanedAsync
            // بس بتعلّم الصف للحذف في الـ change tracker، ولسه ماتحفظش؛
            // استعلام GetStageTotalsUpToAsync بيقرا من قاعدة البيانات
            // فعليًا، فمن غير الحفظة ده هيرجّع الرقم القديم زي ما هو
            await _productionRepo.SaveChangesAsync();

            // الحذف ده يقدر يقلّل الفجوة الحقيقية بين مرحلتين (أو يلغيها
            // خالص) — أي رصيد أولي تلقائي اتعمل عشان الفجوة دي لازم يتصغّر
            // أو يتشال معاها، وإلا فضل يمثّل شغل ناقص مالوش وجود حقيقي
            if (stage is not null)
                await _productionFlow.ReconcileAutoBalancesAsync(stage.ProductId, date);

            await CleanupOrphanedAttendanceAsync(workerId, date);
            await _productionRepo.SaveChangesAsync();

            await transaction.CommitAsync();

            return result;
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
            var affectedWorkerIds = records.Select(r => r.WorkerId).Distinct().ToList();
            var affectedStageIds = records.Select(r => r.ProductionStageId).Distinct().ToList();

            foreach (var record in records)
            {
                // نفس استثناء DeleteProductionAsync: سجل إكمال رصيد أولي
                // ليه InitialBalanceUsage مرتبط بـ FK صارم، لازم يتشال قبله
                var linkedUsage = await FindLinkedInitialBalanceUsageAsync(record.Id);

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
                    removePermanently: () =>
                    {
                        if (linkedUsage is not null) _initialBalanceUsages.Remove(linkedUsage);
                        _productionRepo.Remove(record);
                    });

                // أول رفض بيوقف كل حاجة — المعاملة بتتلغي عند الخروج
                // من غير Commit، فمفيش سجل واحد اتشال
                if (!result.IsDeleted) return result;

                wasNotConfigured = result.PasswordNotConfigured;
            }

            // لازم يتحفظ حذف كل السجلات الأول عشان الفحوصات اللي بعده
            // (هنا وفي CleanupOrphanedAttendanceAsync) تقرا من قاعدة
            // البيانات فعليًا (نفس سبب الحفظين في DeleteProductionAsync)
            await _productionRepo.SaveChangesAsync();

            // كل إنتاج اليوم ده اتشال بالكامل — أي مرحلة من دول بقى
            // مالهاش أي سجل فيه خالص، فالإنتاج الفعلي المرتبط بيها (لو
            // موجود) بقى شبح ويتشال معاه
            foreach (var stageId in affectedStageIds)
                await _productionOutput.RemoveIfNowOrphanedAsync(stageId, date);

            // لازم يتحفظ قبل ReconcileAutoBalancesAsync لنفس سبب
            // DeleteProductionAsync — RemoveIfNowOrphanedAsync لسه ماتحفظش
            await _productionRepo.SaveChangesAsync();

            // نفس منطق DeleteProductionAsync: حذف يوم كامل يقدر يلغي فجوات
            // كانت اتحسبت بين مراحل أكتر من منتج — نعيد فحصها لكل منتج
            // اتأثرت مراحله
            var affectedProductIds = new HashSet<int>();
            foreach (var stageId in affectedStageIds)
                if (await _stageRepo.GetByIdAsync(stageId) is { } affectedStage)
                    affectedProductIds.Add(affectedStage.ProductId);

            foreach (var productId in affectedProductIds)
                await _productionFlow.ReconcileAutoBalancesAsync(productId, date);

            // كل إنتاج اليوم ده اتشال بالكامل — أي عامل من دول بقى
            // مالوش أي إنتاج فيه خالص، فسجل حضوره التلقائي (لو مالوش
            // شغل بالساعة يبرره) بيتشال معاه
            foreach (var workerId in affectedWorkerIds)
                await CleanupOrphanedAttendanceAsync(workerId, date);

            await _productionRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return SoftDeleteResult.Success(wasNotConfigured);
        }

        /// <summary>سجل إكمال رصيد أولي (لو موجود) لسجل إنتاج معيّن — يُشال قبل السجل نفسه بسبب Restrict FK</summary>
        private async Task<InitialBalanceUsage?> FindLinkedInitialBalanceUsageAsync(int dailyProductionId) =>
            (await _initialBalanceUsages.FindAsync(u => u.DailyProductionId == dailyProductionId)).FirstOrDefault();
    }
}
