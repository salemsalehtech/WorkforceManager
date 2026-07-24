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
    /// 3) بتسجل سجل إنتاج لكل (عامل، مرحلة) بكوتة المرحلة وقت التسجيل
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

        public ProductionFlowService(
            IProductRepository productRepo,
            IWorkerRepository workerRepo,
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo)
        {
            _productRepo = productRepo;
            _workerRepo = workerRepo;
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
        }

        /// <summary>
        /// يسجل رحلة إنتاج كاملة ليوم واحد على منتج واحد. بيرمي استثناء
        /// برسالة عربية واضحة لو فيه أي خطأ في المدخلات — ومفيش أي حاجة
        /// بتتحفظ إلا لو الرحلة كلها سليمة.
        /// </summary>
        public async Task<FlowSaveResultDto> RecordFlowAsync(
            int productId, DateTime date,
            IReadOnlyList<FlowRangeDto> ranges,
            IReadOnlyList<FlowShareDto> shares)
        {
            if (ranges.Count == 0)
                throw new InvalidOperationException("سجّل نطاق إنتاج واحد على الأقل (من مرحلة إلى مرحلة بعدد قطع)");
            if (shares.Count == 0)
                throw new InvalidOperationException("وزّع العمال على المراحل الأول قبل الحفظ");

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
            foreach (var range in ranges)
            {
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

            // ---------- 4) إنشاء سجلات الإنتاج (Snapshot للكوتة زي أي تسجيل) ----------
            var stageById = orderedStages.ToDictionary(s => s.Id);
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

            // ---------- 5) حضور تلقائي لمن شارك ومالوش سجل حضور في اليوم ----------
            var existingAttendance = (await _attendanceRepo.GetByDateAsync(date))
                .Select(a => a.WorkerId)
                .ToHashSet();

            var participatingWorkers = shares.Select(s => s.WorkerId).Distinct().ToList();
            var attendanceMarked = 0;
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

            // ---------- 6) بناء ملخص النتيجة (لرسالة النجاح) ----------
            var workerTotals = shares
                .GroupBy(s => s.WorkerId)
                .Select(g => new FlowWorkerTotalDto
                {
                    WorkerName = workersById[g.Key].FullName,
                    TotalPieces = g.Sum(s => s.PieceCount),
                    // حماية من القسمة على صفر (زي DailyProduction.WorkdaysCompleted) —
                    // الكوتة مفروض دايمًا > 0 بالتحقق، بس ده أمان لو البيانات اتبوّظت
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
                WorkerTotals = workerTotals
            };
        }
    }
}
