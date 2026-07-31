using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// خدمة "رحلة الإنتاج اليومية" — الطريقة الأساسية لتسجيل الإنتاج:
    /// المستخدم بيختار المنتج، بيوزّع عامل (أو أكتر) على كل مرحلة من
    /// مراحله المرتبة، وبيسجل الإنتاج كنطاقات: "من المرحلة كذا للمرحلة
    /// كذا اتنتج عدد معين" — والخدمة بتتولى الباقي:
    ///
    /// 1) بتحسب إنتاج كل مرحلة من النطاقات (كل مرحلة في النطاق بتاخد عدده).
    /// 2) بتتحقق من كل حاجة: النطاقات بترتيب صحيح ومش متداخلة، كل مرحلة
    ///    مغطاة عليها عمال، مجموع أنصبة عمال المرحلة = إنتاجها بالظبط،
    ///    وكل عامل مؤهل فعلاً لمرحلته (قرار متفق عليه: المؤهلين بس إجباري).
    /// 3) بتسجل سجل إنتاج لكل (عامل، مرحلة) بيومية المرحلة وقت التسجيل
    ///    (Snapshot) — فاليوميات بتتحسب لكل عامل أوتوماتيك.
    /// 4) بتسجل حضور "حاضر" تلقائيًا لأي عامل شارك ومالوش سجل حضور في
    ///    اليوم (من غير ما تلمس أي سجل حضور موجود بالفعل).
    ///
    /// كل ده بيتحفظ في حفظة واحدة (Transaction واحدة) — يا كله يا مفيش.
    /// </summary>
    public class ProductionFlowService
    {
        private readonly IProductRepository _productRepo;
        private readonly IWorkerRepository _workerRepo;
        private readonly IDailyProductionRepository _productionRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly ProductionBatchService _batchService;
        private readonly WorkerAssignmentGuard _assignmentGuard;
        private readonly IUnitOfWork _unitOfWork;

        public ProductionFlowService(
            IProductRepository productRepo,
            IWorkerRepository workerRepo,
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo,
            IProductionDayClosureRepository closureRepo,
            ProductionBatchService batchService,
            WorkerAssignmentGuard assignmentGuard,
            IUnitOfWork unitOfWork)
        {
            _productRepo = productRepo;
            _workerRepo = workerRepo;
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
            _closureRepo = closureRepo;
            _batchService = batchService;
            _assignmentGuard = assignmentGuard;
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// بيدوّر على آخر يوم اتسجل فيه إنتاج على المنتج ده **قبل** اليوم
        /// المحدد، وبيرجّع توزيع العمال على المراحل زي ما كان.
        ///
        /// الغرض: الشغل اليومي في المصنع بيتكرر — نفس المنتج ونفس الطاقم
        /// تقريبًا كل يوم، والفرق بيبقى في الأعداد بس. الزرار اللي بيستخدم
        /// الدالة دي بيوفّر إعادة توزيع عشرات العمال بالإيد كل صباح.
        ///
        /// بيرجّع null لو مفيش أي إنتاج على المنتج في فترة البحث.
        /// </summary>
        /// <param name="lookbackDays">
        /// أقصى رجوع للخلف. محدود عشان مانحمّلش تاريخ المنتج كله —
        /// لو المنتج مااشتغلش من شهرين يبقى التكرار مالوش معنى أصلاً.
        /// </param>
        public async Task<LastFlowDto?> GetLastFlowAsync(int productId, DateTime before, int lookbackDays = 60)
        {
            var product = await _productRepo.GetWithStagesAsync(productId);
            if (product is null) return null;

            var stageIds = product.Stages.Select(s => s.Id).ToHashSet();
            if (stageIds.Count == 0) return null;

            var from = before.Date.AddDays(-lookbackDays);
            var to = before.Date.AddDays(-1); // قبل اليوم المحدد، مش هو نفسه

            var records = (await _productionRepo.GetByRangeAsync(from, to))
                .Where(r => stageIds.Contains(r.ProductionStageId))
                .ToList();

            if (records.Count == 0) return null;

            // آخر يوم فيه شغل على المنتج ده
            var lastDate = records.Max(r => r.Date.Date);

            var assignments = records
                .Where(r => r.Date.Date == lastDate)
                // نفس العامل ممكن يكون له أكتر من سجل على نفس المرحلة — مرة واحدة تكفي
                .GroupBy(r => (r.ProductionStageId, r.WorkerId))
                .Select(g => new FlowAssignmentDto
                {
                    ProductionStageId = g.Key.ProductionStageId,
                    WorkerId = g.Key.WorkerId,
                    WorkerName = g.First().Worker?.FullName ?? string.Empty
                })
                .ToList();

            return new LastFlowDto { Date = lastDate, Assignments = assignments };
        }

        /// <summary>
        /// يسجل رحلة إنتاج كاملة ليوم واحد على منتج واحد. بيرمي استثناء
        /// برسالة عربية واضحة لو فيه أي خطأ في المدخلات — ومفيش أي حاجة
        /// بتتحفظ إلا لو الرحلة كلها سليمة.
        /// </summary>
        /// <param name="confirmOverride">
        /// المستخدم شاف تحذير "العامل مكلّف بمنتج/مرحلة تانية النهارده"
        /// ووافق صراحة. بيخص النداء ده بس — ومبيسمحش بالتكرار الحرفي
        /// (نفس العامل ونفس المرحلة ونفس اليوم) اللي بيفضل ممنوع.
        /// من غيره، أول تعارض بيرمي
        /// <see cref="AssignmentConfirmationRequiredException"/> **قبل**
        /// أي كتابة، والواجهة بتسأل المستخدم وتعيد النداء بـ true.
        /// </param>
        public async Task<FlowSaveResultDto> RecordFlowAsync(
            int productId, DateTime date,
            IReadOnlyList<BatchRangeDto> ranges,
            IReadOnlyList<FlowShareDto> shares,
            bool confirmOverride = false)
        {
            if (ranges.Count == 0)
                throw new InvalidOperationException("سجّل نطاق إنتاج واحد على الأقل (من مرحلة إلى مرحلة بعدد قطع)");
            if (shares.Count == 0)
                throw new InvalidOperationException("وزّع العمال على المراحل الأول قبل الحفظ");

            // يوم مقفول = المستخدم راجع أرقامه ووافق على ترحيل الواقف.
            // فتح الباب لتسجيل جديد بعد كده بيخلي تقرير مطبوع يكدب
            if (await _closureRepo.IsClosedAsync(date))
                throw new InvalidOperationException(
                    $"إنتاج يوم {date:yyyy/MM/dd} مقفول — افتح اليوم تاني من شاشة التسجيل لو محتاج تعدّل");

            // ---------- 1) تحميل المنتج ومراحله النشطة بترتيب خط الإنتاج ----------
            var product = await _productRepo.GetWithStagesAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            var orderedStages = product.Stages
                .Where(s => s.IsActive)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();
            if (orderedStages.Count == 0)
                throw new InvalidOperationException($"المنتج \"{product.Name}\" ليس له مراحل نشطة");

            // فهرس كل مرحلة في الترتيب (بنعتمد على موقعها في القائمة المرتبة، مش على قيمة SortOrder نفسها)
            var indexByStageId = orderedStages
                .Select((stage, index) => (stage.Id, index))
                .ToDictionary(x => x.Id, x => x.index);

            // ---------- 2) حساب إنتاج كل مرحلة من النطاقات + منع التداخل ----------
            var piecesPerStage = new int[orderedStages.Count];

            // كل مرحلة بتنتمي لنطاق واحد بالظبط (النطاقات مش بتتداخل)، والنطاق
            // بينتمي لدفعة واحدة — فالخريطة دي بتوصّل كل سجل إنتاج بدفعته
            var rangeIndexByStageId = new Dictionary<int, int>();

            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];

                if (!indexByStageId.TryGetValue(range.FromStageId, out var fromIndex) ||
                    !indexByStageId.TryGetValue(range.ToStageId, out var toIndex))
                    throw new InvalidOperationException("نطاق إنتاج بيشاور على مرحلة مش من مراحل المنتج المحدد");

                if (fromIndex > toIndex)
                    throw new InvalidOperationException(
                        $"نطاق غير صحيح: \"{orderedStages[fromIndex].StageName}\" بتيجي بعد \"{orderedStages[toIndex].StageName}\" في خط الإنتاج — راجع الترتيب");

                if (range.PieceCount <= 0)
                    throw new InvalidOperationException("عدد القطع في كل نطاق لازم يكون رقمًا موجبًا");

                for (var i = fromIndex; i <= toIndex; i++)
                {
                    // نفس المرحلة ميصحش تقع في نطاقين — ده تسجيل مزدوج هيبوّظ اليوميات
                    if (piecesPerStage[i] != 0)
                        throw new InvalidOperationException(
                            $"المرحلة \"{orderedStages[i].StageName}\" واقعة في أكتر من نطاق — النطاقات ميصحش تتداخل");

                    piecesPerStage[i] = range.PieceCount;
                    rangeIndexByStageId[orderedStages[i].Id] = r;
                }
            }

            // نطاقين في نفس الحفظة مينفعش يكمّلوا نفس الدفعة — التاني هيلاقيها
            // اتحركت خلاص، والمجموع هيتعد مرتين
            var duplicateBatch = ranges
                .Where(r => r.BatchId is not null)
                .GroupBy(r => r.BatchId!.Value)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateBatch is not null)
                throw new InvalidOperationException("نفس الدفعة متحطّة في أكتر من نطاق — اجمعهم في نطاق واحد");

            // ---------- 3) التحقق من توزيع العمال على المراحل ----------
            // المؤهلين لكل مراحل المنتج باستعلام واحد (القرار المتفق عليه: المؤهلين بس)
            var productSkills = await _workerRepo.GetSkillsForProductAsync(productId);
            var qualifiedPairs = productSkills
                .Select(ws => (ws.ProductionStageId, ws.WorkerId))
                .ToHashSet();
            var workersById = productSkills
                .GroupBy(ws => ws.WorkerId)
                .ToDictionary(g => g.Key, g => g.First().Worker);

            var seenPairs = new HashSet<(int StageId, int WorkerId)>();
            foreach (var share in shares)
            {
                if (!indexByStageId.TryGetValue(share.ProductionStageId, out var stageIndex))
                    throw new InvalidOperationException("توزيع عامل بيشاور على مرحلة مش من مراحل المنتج المحدد");

                var stageName = orderedStages[stageIndex].StageName;

                if (!seenPairs.Add((share.ProductionStageId, share.WorkerId)))
                    throw new InvalidOperationException($"نفس العامل متسجل مرتين على مرحلة \"{stageName}\"");

                if (share.PieceCount <= 0)
                    throw new InvalidOperationException($"نصيب كل عامل في مرحلة \"{stageName}\" لازم يكون رقمًا موجبًا");

                if (piecesPerStage[stageIndex] == 0)
                    throw new InvalidOperationException(
                        $"مرحلة \"{stageName}\" عليها عمال لكن مش داخلة في أي نطاق إنتاج — إما ضيفها لنطاق أو شيل عمالها");

                if (!qualifiedPairs.Contains((share.ProductionStageId, share.WorkerId)))
                    throw new InvalidOperationException(
                        $"فيه عامل غير مؤهل لمرحلة \"{stageName}\" — اربط المهارة من شاشة العمال الأول");
            }

            // كل مرحلة مغطاة بنطاق: لازم يكون عليها عمال، ومجموع أنصبتهم = إنتاجها بالظبط
            var sharesByStage = shares.ToLookup(s => s.ProductionStageId);
            for (var i = 0; i < orderedStages.Count; i++)
            {
                if (piecesPerStage[i] == 0) continue; // مرحلة مش داخلة في الرحلة النهارده — عادي

                var stage = orderedStages[i];
                var stageShares = sharesByStage[stage.Id].ToList();

                if (stageShares.Count == 0)
                    throw new InvalidOperationException(
                        $"مرحلة \"{stage.StageName}\" عليها إنتاج ({piecesPerStage[i]} قطعة) لكن مفيش عامل متوزع عليها");

                var sum = stageShares.Sum(s => s.PieceCount);
                if (sum != piecesPerStage[i])
                    throw new InvalidOperationException(
                        $"مرحلة \"{stage.StageName}\": مجموع توزيع العمال ({sum}) لا يساوي إنتاج المرحلة ({piecesPerStage[i]})");
            }

            var stageById = orderedStages.ToDictionary(s => s.Id);
            int attendanceMarked;
            var movements = new List<BatchMovementDto>();

            // ---------- 4) قاعدة التكليف + الكتابة، الاتنين جوه معاملة واحدة ----------
            // القفل بيتاخد من أول لحظة، فالتحقق بيتم على بيانات مش ممكن
            // نسخة تانية من البرنامج تغيّرها قبل ما نخلّص كتابة (منع سباق)
            await using (var transaction = await _unitOfWork.BeginWriteTransactionAsync())
            {
                var requestedAssignments = shares
                    .Select(share => new WorkerAssignmentDto
                    {
                        WorkerId = share.WorkerId,
                        WorkerName = workersById[share.WorkerId].FullName,
                        ProductId = product.Id,
                        ProductName = product.Name,
                        ProductionStageId = share.ProductionStageId,
                        StageName = stageById[share.ProductionStageId].StageName
                    })
                    .ToList();

                // القاعدة المشتركة (المصدر الوحيد) — بترمي قبل أي كتابة،
                // والمعاملة بتترجع تلقائيًا بالـ Dispose فمفيش أثر خالص
                var assignmentCheck = await _assignmentGuard.CheckAsync(date, requestedAssignments);
                WorkerAssignmentGuard.EnsureAllowed(assignmentCheck, confirmOverride);

                // ---------- ربط كل نطاق بدفعته (جوه القفل) ----------
                // التحقق لازم يتم تحت نفس قفل الكتابة: من غير كده نسختين من
                // البرنامج ممكن يقروا نفس الدفعة الواقفة ويكمّلوها الاتنين
                var batchPerRange = new ProductionBatch[ranges.Count];
                for (var r = 0; r < ranges.Count; r++)
                    batchPerRange[r] = await _batchService.ResolveForRangeAsync(product, ranges[r], date);

                // ---------- إنشاء سجلات الإنتاج (Snapshot لليومية زي أي تسجيل) ----------
                foreach (var share in shares)
                {
                    var stage = stageById[share.ProductionStageId];
                    await _productionRepo.AddAsync(new DailyProduction
                    {
                        WorkerId = share.WorkerId,
                        ProductionStageId = share.ProductionStageId,
                        Date = date.Date,
                        PieceCount = share.PieceCount,
                        PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday,
                        // بالـ navigation مش بالـ Id: الدفعة الجديدة لسه مالهاش
                        // Id قبل الحفظ، وEF بيربط المفتاح لوحده وقت الإدراج
                        ProductionBatch = batchPerRange[rangeIndexByStageId[share.ProductionStageId]]
                    });
                }

                // ---------- تحريك الدفعات (قسمة لو التكميل جزئي، وقفل لو خلصت) ----------
                for (var r = 0; r < ranges.Count; r++)
                    movements.Add(await _batchService.AdvanceAsync(
                        batchPerRange[r], product, ranges[r].ToStageId, ranges[r].PieceCount, date));

                // ---------- 5) حضور تلقائي لمن شارك ومالوش سجل حضور في اليوم ----------
                var existingAttendance = (await _attendanceRepo.GetByDateAsync(date))
                    .Select(a => a.WorkerId)
                    .ToHashSet();

                var participatingWorkers = shares.Select(s => s.WorkerId).Distinct().ToList();
                attendanceMarked = 0;
                foreach (var workerId in participatingWorkers.Where(id => !existingAttendance.Contains(id)))
                {
                    await _attendanceRepo.AddAsync(new Attendance
                    {
                        WorkerId = workerId,
                        Date = date.Date,
                        Status = AttendanceStatus.Present
                    });
                    attendanceMarked++;
                }

                // حفظة واحدة لكل حاجة (الريبوهات بتشارك نفس الـ DbContext في نفس الـ Scope)
                await _productionRepo.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            // ---------- 6) بناء ملخص النتيجة (لرسالة النجاح) ----------
            var workerTotals = shares
                .GroupBy(s => s.WorkerId)
                .Select(g => new FlowWorkerTotalDto
                {
                    WorkerName = workersById[g.Key].FullName,
                    TotalPieces = g.Sum(s => s.PieceCount),
                    // حماية من القسمة على صفر (زي DailyProduction.WorkdaysCompleted) —
                    // اليومية مفروض دايمًا > 0 بالتحقق، بس ده أمان لو البيانات اتبوّظت
                    TotalWorkdays = Math.Round(g.Sum(s =>
                    {
                        var quota = stageById[s.ProductionStageId].PiecesPerWorkday;
                        return quota == 0 ? 0m : (decimal)s.PieceCount / quota;
                    }), 2)
                })
                .OrderByDescending(t => t.TotalWorkdays)
                .ToList();

            return new FlowSaveResultDto
            {
                RecordsCount = shares.Count,
                StagesCovered = piecesPerStage.Count(p => p > 0),
                AttendanceMarkedCount = attendanceMarked,
                WorkerTotals = workerTotals,
                BatchMovements = movements
            };
        }
    }
}
