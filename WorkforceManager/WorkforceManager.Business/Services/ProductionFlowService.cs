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
    ///    مغطاة عليها عمال، وكل عامل مؤهل فعلاً لمرحلته (قرار متفق عليه:
    ///    المؤهلين بس إجباري). **مجموع أنصبة العمال مايتحققش من إنتاج
    ///    المرحلة عن قصد** — قطعة العامل عدد ضرباته على المكنة (أساس
    ///    يوميته)، ومش لازم تساوي الإنتاج الفعلي، شوف
    ///    <see cref="ProductionStageOutputService"/>.
    /// 3) بتسجل سجل إنتاج لكل (عامل، مرحلة) بيومية المرحلة وقت التسجيل
    ///    (Snapshot) — فاليوميات بتتحسب لكل عامل أوتوماتيك. وبتسجل
    ///    الإنتاج الفعلي لكل مرحلة مغطاة (رقم النطاق نفسه) منفصلًا تمامًا.
    /// 4) أي عامل رص/تدريب متحط تاج على مرحلة لليوم بس (<paramref
    ///    name="taggedWorkers"/> في <see cref="RecordFlowAsync"/>) بياخد
    ///    حاضر بيومية شيفت عادي (1) تلقائيًا، من غير قطع ومن غير تحقق
    ///    تأهيل، ومنفصل تمامًا عن أنصبة العمال بالقطعة
    ///    (<see cref="FlowShareDto"/>). مرحلة الرص نفسها
    ///    (<see cref="ProductionStage.IsRackingStage"/>) بتوصل هنا بنفس
    ///    الطريقة — مرحلة عادية في الشاشة تاجها عامل، مش استثناء —
    ///    والعامل الثابت بتاع المنتج (Product.RackingWorkerId) بس
    ///    افتراضي بيتحط تاجه في الواجهة كل يوم، مش بيتسجل من هنا تلقائيًا.
    /// 5) بتسجل حضور "حاضر" تلقائيًا لأي عامل شارك ومالوش سجل حضور في
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
        private readonly WorkerAssignmentGuard _assignmentGuard;
        private readonly OperationsPasswordService _gate;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ActivityLogService _log;
        private readonly ProductionStageOutputService _productionOutput;
        private readonly HourlyWorkdayService _hourlyWorkdayService;

        public ProductionFlowService(
            IProductRepository productRepo,
            IWorkerRepository workerRepo,
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo,
            IProductionDayClosureRepository closureRepo,
            WorkerAssignmentGuard assignmentGuard,
            OperationsPasswordService gate,
            IUnitOfWork unitOfWork,
            ActivityLogService log,
            ProductionStageOutputService productionOutput,
            HourlyWorkdayService hourlyWorkdayService)
        {
            _log = log;
            _productRepo = productRepo;
            _workerRepo = workerRepo;
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
            _closureRepo = closureRepo;
            _assignmentGuard = assignmentGuard;
            _gate = gate;
            _unitOfWork = unitOfWork;
            _productionOutput = productionOutput;
            _hourlyWorkdayService = hourlyWorkdayService;
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
        /// الأيام اللي فيها شغل فعلي على المنتج ده قبل اليوم المحدد،
        /// الأحدث الأول، ومعاها عدد العمال والمراحل في كل يوم.
        ///
        /// دي اللي بتغذّي اختيار اليوم في "كرّر يوم فات": منتقي تاريخ
        /// فاضي بيخلي المستخدم يخمّن ويلاقي "مفيش شغل في اليوم ده"
        /// كذا مرة. القايمة بتوريه الأيام اللي فيها حاجة تتكرر أصلاً.
        /// </summary>
        public async Task<IReadOnlyList<FlowDayOptionDto>> GetRepeatableDaysAsync(
            int productId, DateTime before, int lookbackDays = 60)
        {
            var product = await _productRepo.GetWithStagesAsync(productId);
            if (product is null) return Array.Empty<FlowDayOptionDto>();

            var stageIds = product.Stages.Select(s => s.Id).ToHashSet();
            if (stageIds.Count == 0) return Array.Empty<FlowDayOptionDto>();

            var from = before.Date.AddDays(-lookbackDays);
            var to = before.Date.AddDays(-1); // قبل اليوم المحدد، مش هو نفسه

            return (await _productionRepo.GetByRangeAsync(from, to))
                .Where(r => stageIds.Contains(r.ProductionStageId))
                .GroupBy(r => r.Date.Date)
                .Select(g => new FlowDayOptionDto
                {
                    Date = g.Key,
                    WorkerCount = g.Select(r => r.WorkerId).Distinct().Count(),
                    StageCount = g.Select(r => r.ProductionStageId).Distinct().Count()
                })
                .OrderByDescending(d => d.Date)
                .ToList();
        }

        /// <summary>
        /// توزيع العمال على المراحل في **يوم محدد** بالظبط (مش آخر يوم).
        /// بيرجّع null لو اليوم ده مفيهوش شغل على المنتج.
        /// </summary>
        public async Task<LastFlowDto?> GetFlowOnAsync(int productId, DateTime date)
        {
            var product = await _productRepo.GetWithStagesAsync(productId);
            if (product is null) return null;

            var stageIds = product.Stages.Select(s => s.Id).ToHashSet();
            if (stageIds.Count == 0) return null;

            var records = (await _productionRepo.GetByRangeAsync(date.Date, date.Date))
                .Where(r => stageIds.Contains(r.ProductionStageId))
                .ToList();

            if (records.Count == 0) return null;

            var assignments = records
                // نفس العامل ممكن يكون له أكتر من سجل على نفس المرحلة — مرة واحدة تكفي
                .GroupBy(r => (r.ProductionStageId, r.WorkerId))
                .Select(g => new FlowAssignmentDto
                {
                    ProductionStageId = g.Key.ProductionStageId,
                    WorkerId = g.Key.WorkerId,
                    WorkerName = g.First().Worker?.FullName ?? string.Empty
                })
                .ToList();

            return new LastFlowDto { Date = date.Date, Assignments = assignments };
        }

        /// <summary>
        /// يسجل رحلة إنتاج كاملة ليوم واحد على منتج واحد. بيرمي استثناء
        /// برسالة عربية واضحة لو فيه أي خطأ في المدخلات — ومفيش أي حاجة
        /// بتتحفظ إلا لو الرحلة كلها سليمة.
        /// </summary>
        /// <param name="taggedWorkers">
        /// عمال رص/تدريب متحطين تاج على مراحل لليوم بس — بلا قطع وبلا
        /// تحقق تأهيل، ومنفصلين تمامًا عن <paramref name="shares"/>. مفيش
        /// عليهم أي قاعدة تكليف (WorkerAssignmentGuard) لأنهم مش بيشتغلوا
        /// بالقطعة أصلًا، شوف <see cref="FlowTaggedWorkerDto"/>.
        /// </param>
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
            IReadOnlyList<FlowRangeDto> ranges,
            IReadOnlyList<FlowShareDto> shares,
            IReadOnlyList<FlowTaggedWorkerDto>? taggedWorkers = null,
            bool confirmOverride = false,
            string operationsPassword = "")
        {
            if (ranges.Count == 0)
                throw new InvalidOperationException("سجّل نطاق إنتاج واحد على الأقل (من مرحلة إلى مرحلة بعدد قطع)");

            // الإنتاج هو اللي اليوميات بتتحسب منه، واليوميات هي الأجر —
            // فالتسجيل عملية بتلمس فلوس زي أي واحدة تانية في القايمة
            var gate = await _gate.VerifyAsync(SensitiveAction.RecordProduction, operationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);
            if (shares.Count == 0)
                throw new InvalidOperationException("وزّع العمال على المراحل الأول قبل الحفظ");

            // يوم مقفول = المستخدم راجع أرقامه ووافق عليها. فتح الباب
            // لتسجيل جديد بعد كده بيخلي تقرير مطبوع يكدب
            if (await _closureRepo.IsClosedAsync(date))
                throw new InvalidOperationException(DayClosureService.ClosedDayMessage(date));

            // ---------- 1) تحميل المنتج ومراحله النشطة بترتيب خط الإنتاج ----------
            var product = await _productRepo.GetWithStagesAsync(productId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            // ProductionLine.Active بيستبعد مرحلة الرص — فمرحلة الرص مستحيل
            // تدخل نطاق أو تتحقق كأنها مرحلة إنتاج عادية، حتى لو حصل خطأ
            // في الواجهة وحاولت تبعتها
            var orderedStages = ProductionLine.Active(product);
            if (orderedStages.Count == 0)
                throw new InvalidOperationException($"المنتج \"{product.Name}\" ليس له مراحل نشطة");

            // فهرس كل مرحلة في الترتيب (بنعتمد على موقعها في القائمة المرتبة، مش على قيمة SortOrder نفسها)
            var indexByStageId = orderedStages
                .Select((stage, index) => (stage.Id, index))
                .ToDictionary(x => x.Id, x => x.index);

            // ---------- 2) حساب إنتاج كل مرحلة من النطاقات + منع التكرار ----------
            var piecesPerStage = new int[orderedStages.Count];

            // أنهي نطاق حجز أنهي مرحلة. الرقم ده بيدخل في رسالة الخطأ:
            // "متسجلة في النطاق رقم 1" أنفع بكتير من "فيه تداخل" لما يكون
            // المستخدم كاتب 4 نطاقات وبيدوّر على الغلط فيهم
            var claimedByRange = new int[orderedStages.Count];

            var rangeList = ranges.ToList();
            for (var rangeNumber = 0; rangeNumber < rangeList.Count; rangeNumber++)
            {
                var range = rangeList[rangeNumber];

                if (!indexByStageId.TryGetValue(range.FromStageId, out var fromIndex) ||
                    !indexByStageId.TryGetValue(range.ToStageId, out var toIndex))
                    throw new InvalidOperationException(
                        $"النطاق رقم {rangeNumber + 1} بيشاور على مرحلة مش من مراحل المنتج المحدد");

                if (fromIndex > toIndex)
                    throw new InvalidOperationException(
                        $"النطاق رقم {rangeNumber + 1} معكوس: \"{orderedStages[fromIndex].StageName}\" بتيجي بعد " +
                        $"\"{orderedStages[toIndex].StageName}\" في خط الإنتاج — راجع الترتيب");

                if (range.PieceCount <= 0)
                    throw new InvalidOperationException(
                        $"عدد القطع في النطاق رقم {rangeNumber + 1} لازم يكون رقمًا موجبًا");

                for (var i = fromIndex; i <= toIndex; i++)
                {
                    // نفس المرحلة ميصحش تقع في نطاقين — ده تسجيل مزدوج
                    // بيضاعف يوميات العمال وأجورهم
                    if (piecesPerStage[i] != 0)
                        throw new InvalidOperationException(
                            $"المرحلة \"{orderedStages[i].StageName}\" متسجلة خلاص في النطاق رقم " +
                            $"{claimedByRange[i] + 1}، ومش هينفع تتسجل تاني في النطاق رقم {rangeNumber + 1} — " +
                            $"المرحلة الواحدة بتتحسب مرة واحدة في اليوم");

                    piecesPerStage[i] = range.PieceCount;
                    claimedByRange[i] = rangeNumber;
                }
            }

            // ---------- 3) التحقق من توزيع العمال على المراحل ----------
            // المؤهلين لكل مراحل المنتج باستعلام واحد (القرار المتفق عليه: المؤهلين بس)
            var productSkills = await _workerRepo.GetSkillsForProductAsync(productId);
            var qualifiedPairs = productSkills
                .Select(ws => (ws.ProductionStageId, ws.WorkerId))
                .ToHashSet();
            var workersById = productSkills
                .GroupBy(ws => ws.WorkerId)
                .ToDictionary(g => g.Key, g => g.First().Worker);

            // عمال رص/تدريب متحطين تاج على مراحل لليوم بس — بلا قطع وبلا
            // تحقق تأهيل عن قصد. التحقق هنا على **كل** مراحل المنتج
            // (مش orderedStages بس) عشان مرحلة الرص نفسها — المستبعدة من
            // خط الإنتاج المحسوب — تقبل تاج عليها، شوف
            // <see cref="ProductionStage.IsRackingStage"/>. مبني هنا فوق
            // (مش تحت زي ما كان) عشان taggedStageIds يتستخدم في تحقق
            // توزيع العمال جاي تحت.
            var taggedList = taggedWorkers ?? Array.Empty<FlowTaggedWorkerDto>();
            var allStageIds = product.Stages.Select(s => s.Id).ToHashSet();
            foreach (var tagged in taggedList)
                if (!allStageIds.Contains(tagged.ProductionStageId))
                    throw new InvalidOperationException(
                        "عامل رص/تدريب متحط تاج على مرحلة مش من مراحل المنتج المحدد");

            var taggedStageIds = taggedList.Select(t => t.ProductionStageId).ToHashSet();

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

            // كل مرحلة مغطاة بنطاق: لازم يكون عليها عمال، ومجموع أنصبتهم = إنتاجها بالظبط.
            // الاستثناء الوحيد: مرحلة عليها متدرّب بس (تاج بلا نصيب قطع) —
            // القطع فعلاً طلعت من المرحلة (بتتسجل في ProductionStageOutputService
            // تحت زي أي مرحلة تانية)، بس محدش حقيقي عليها ياخد أجر عنها.
            // "مفيش عامل" رسالة غلط هنا: فيه عامل، هو بس بالساعة مش بالقطعة.
            var sharesByStage = shares.ToLookup(s => s.ProductionStageId);
            for (var i = 0; i < orderedStages.Count; i++)
            {
                if (piecesPerStage[i] == 0) continue; // مرحلة مش داخلة في الرحلة النهارده — عادي

                var stage = orderedStages[i];
                var stageShares = sharesByStage[stage.Id].ToList();

                if (stageShares.Count == 0 && !taggedStageIds.Contains(stage.Id))
                    throw new InvalidOperationException(
                        $"مرحلة \"{stage.StageName}\" عليها إنتاج ({piecesPerStage[i]} قطعة) لكن مفيش عامل متوزع عليها");

                // مجموع توزيع العمال عن قصد **مايتحققش** من إنتاج المرحلة:
                // قطعة العامل عدد ضرباته على المكنة (أساس يوميته وأجره)،
                // ومش لازم تساوي الإنتاج الفعلي — جزء من الضربات بيتحول
                // هالك أو مايكملش. الرقمين منفصلين تمامًا، شوف
                // ProductionStageOutputService.
            }

            var stageById = orderedStages.ToDictionary(s => s.Id);
            int attendanceMarked;

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
                        PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday
                    });
                }

                // ---------- الإنتاج الفعلي لكل مرحلة مغطاة — منفصل تمامًا عن نصيب العمال ----------
                // نفس رقم النطاق بيروح لكل مرحلة فيه (زي ما كان بيتحقق منه
                // بس من غير تخزين). تراكمي: رحلة تانية على نفس المرحلة/اليوم
                // (بعد إعادة فتح يوم مقفول مثلًا) بتتجمّع، ماتستبدلش.
                for (var i = 0; i < orderedStages.Count; i++)
                {
                    if (piecesPerStage[i] == 0) continue;
                    await _productionOutput.RecordOutputAsync(orderedStages[i].Id, date, piecesPerStage[i]);
                }

                // ---------- عمال رص/تدريب متحطين تاج على مراحل — بلا قطع ومفيش تحقق تأهيل ----------
                // مرحلة الرص نفسها بتوصل هنا زي أي مرحلة تاجها عامل —
                // العامل الثابت بتاع المنتج (Product.RackingWorkerId) بس
                // افتراضي بيتحط تاجه في الواجهة، مش بيتسجل تلقائيًا من هنا
                foreach (var tagged in taggedList)
                    await _hourlyWorkdayService.RecordHourlyWorkAsync(
                        tagged.WorkerId, date, HourlyWorkdayService.ShiftEndHour);

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
                    // نفس قاعدة WorkdayMath اللي السجل المحفوظ بيتحسب بيها:
                    // كل سجل بيتقرّب لوحده وبعدين بنجمع. لو جمعنا الكسور
                    // الكاملة وقرّبنا مرة واحدة، المعاينة هنا هتقول رقم
                    // والكشف الأسبوعي هيقول رقم تاني لنفس اليوم
                    TotalWorkdays = g.Sum(s => WorkdayMath.FromPieces(
                        s.PieceCount, stageById[s.ProductionStageId].PiecesPerWorkday))
                })
                .OrderByDescending(t => t.TotalWorkdays)
                .ToList();

            // تسجيل الإنتاج هو المصدر الأساسي لكل يوميات العمال وأجورهم،
            // فبيتسجّل بعد ما المعاملة تنجح — لا قبلها (حدث لعملية
            // اترجعت) ولا جواها (السجل بيتلغي معاها لو رجعت)
            await _log.LogAsync(
                ActivityEventType.ProductionRecorded, "Product", product.Id,
                entityName: product.Name,
                details: $"{shares.Sum(s => s.PieceCount):N0} قطعة على "
                         + $"{shares.Select(s => s.WorkerId).Distinct().Count()} عامل "
                         + $"يوم {date:yyyy/MM/dd}");

            return new FlowSaveResultDto
            {
                RecordsCount = shares.Count,
                StagesCovered = piecesPerStage.Count(p => p > 0),
                AttendanceMarkedCount = attendanceMarked,
                WorkerTotals = workerTotals,
                // خلص الخط = اتسجل عليه إنتاج على آخر مرحلة نشطة
                CompletedPieces = piecesPerStage[^1],
                StartedPieces = piecesPerStage[0]
            };
        }
    }
}
