using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// "الرصيد الأولي" لمنتج: كمية قطع تخص تاريخ إنتاج أصلي معيّن ولسه
    /// ماكملتش (شوف <see cref="InitialBalance"/>) — إنشاء، إدارة نطاقات،
    /// وسحب/إكمال.
    ///
    /// **السحب (<see cref="WithdrawAsync"/>) بينادي
    /// <see cref="ProductionFlowService.RecordFlowAsync"/> نفسها** —
    /// رحلة إنتاج عادية بالكامل: نفس التحقق من النطاقات، نفس
    /// WorkerAssignmentGuard، نفس الحضور التلقائي، ونفس تسجيل الإنتاج
    /// الفعلي بتاريخ السحب نفسه (مش تاريخ الرصيد الأصلي، بعكس التصميم
    /// القديم). **مفيش تكرار عد** رغم كده، لأن الرحلة الأصلية الناقصة
    /// أصلًا ما سجّلتش إنتاج فعلي على المراحل اللي بعدها — السحب هو أول
    /// مرة الإنتاج الفعلي بيتسجل عليها. الأجر (<see cref="InitialBalanceUsage"/>)
    /// بيتسجل مرة واحدة بس لكل صف إنتاج **على مرحلة خروج النطاق**
    /// (<see cref="InitialBalanceRange.ToStageId"/>) — مراحل النطاق
    /// الوسيطة بتاخد سجل إنتاج وأجر حقيقي زي أي مرحلة عادية، بس من غير
    /// InitialBalanceUsage خاص بيها، عشان الكمية المستهلكة من النطاق
    /// (وبالتبعية InitialBalance.UsedQuantity) متتعدّش وهي بتتجمّع.
    /// </summary>
    public class InitialBalanceService
    {
        private readonly AppDbContext _db;
        private readonly ActivityLogService _log;
        private readonly ProductionFlowService _productionFlow;
        private readonly ScrapService _scrap;
        private readonly IUnitOfWork _unitOfWork;

        public InitialBalanceService(
            AppDbContext db,
            ActivityLogService log,
            ProductionFlowService productionFlow,
            ScrapService scrap,
            IUnitOfWork unitOfWork)
        {
            _db = db;
            _log = log;
            _productionFlow = productionFlow;
            _scrap = scrap;
            _unitOfWork = unitOfWork;
        }

        // ======================= الإنشاء =======================

        /// <summary>ينشئ رصيدًا أوليًا جديدًا (يدويًا أو من قطع ناقصة برحلة إنتاج)، مع نطاقاته الابتدائية لو موجودة</summary>
        public async Task<InitialBalanceDto> CreateAsync(CreateInitialBalanceRequest request, string? createdBy = null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("اسم الرصيد مطلوب", nameof(request));
            if (request.Quantity <= 0)
                throw new ArgumentException("كمية الرصيد يجب أن تكون رقمًا موجبًا", nameof(request));

            var product = await _db.Products.Include(p => p.Stages)
                .FirstOrDefaultAsync(p => p.Id == request.ProductId)
                ?? throw new InvalidOperationException("المنتج المحدد غير موجود");

            var orderedStages = ProductionLine.Active(product);

            var balance = new InitialBalance
            {
                ProductId = request.ProductId,
                Name = request.Name.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                Quantity = request.Quantity,
                OriginalDate = request.OriginalDate.Date,
                Source = request.Source,
                OriginalDailyProductionId = request.OriginalDailyProductionId,
                CreatedBy = createdBy
            };

            // ترتيب/تداخل كل النطاقات المُقدَّمة مع بعض — نفس منطق نطاقات
            // رحلة الإنتاج العادية بالظبط (شوف StageRangeValidator)
            if (request.Ranges.Count > 0)
                StageRangeValidator.ValidateAndComputePiecesPerStage(orderedStages,
                    request.Ranges
                        .Select(r => new FlowRangeDto { FromStageId = r.FromStageId, ToStageId = r.ToStageId, PieceCount = r.PieceCount })
                        .ToList(), out _);

            var rangesTotal = 0;
            foreach (var rangeReq in request.Ranges)
            {
                rangesTotal += rangeReq.PieceCount;
                if (rangesTotal > request.Quantity)
                    throw new InvalidOperationException(
                        "مجموع كمية النطاقات أكبر من كمية الرصيد الكلية");

                balance.Ranges.Add(new InitialBalanceRange
                {
                    FromStageId = rangeReq.FromStageId,
                    ToStageId = rangeReq.ToStageId,
                    PieceCount = rangeReq.PieceCount,
                    SortOrder = balance.Ranges.Count
                });
            }

            await _db.InitialBalances.AddAsync(balance);
            await _db.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceCreated, "InitialBalance", balance.Id,
                entityName: $"{product.Name} — {balance.Name}",
                details: $"{balance.Quantity:N0} قطعة من تاريخ {balance.OriginalDate:yyyy/MM/dd}");

            return await GetByIdAsync(balance.Id)
                ?? throw new InvalidOperationException("تعذّر إنشاء الرصيد الأولي");
        }

        // ======================= النطاقات =======================

        /// <summary>يضيف نطاقًا جديدًا لرصيد قائم، بشرط ما يتجاوزش كمية الرصيد الكلية</summary>
        public async Task<InitialBalanceRangeDto> AddRangeAsync(int balanceId, AddInitialBalanceRangeRequest request)
        {
            var balance = await _db.InitialBalances
                .Include(b => b.Product).ThenInclude(p => p.Stages)
                .Include(b => b.Ranges)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            var orderedStages = ProductionLine.Active(balance.Product);

            // ترتيب/تداخل النطاق الجديد مع النطاقات المحفوظة فعلاً مع بعض
            var combinedRanges = balance.Ranges
                .Select(r => new FlowRangeDto { FromStageId = r.FromStageId, ToStageId = r.ToStageId, PieceCount = r.PieceCount })
                .Append(new FlowRangeDto { FromStageId = request.FromStageId, ToStageId = request.ToStageId, PieceCount = request.PieceCount })
                .ToList();
            StageRangeValidator.ValidateAndComputePiecesPerStage(orderedStages, combinedRanges, out _);

            var existingTotal = balance.Ranges.Sum(r => r.PieceCount);
            if (existingTotal + request.PieceCount > balance.Quantity)
                throw new InvalidOperationException(
                    $"مجموع كمية النطاقات ({existingTotal + request.PieceCount:N0}) أكبر من كمية الرصيد الكلية ({balance.Quantity:N0})");

            var range = new InitialBalanceRange
            {
                InitialBalanceId = balanceId,
                FromStageId = request.FromStageId,
                ToStageId = request.ToStageId,
                PieceCount = request.PieceCount,
                SortOrder = balance.Ranges.Count
            };

            await _db.InitialBalanceRanges.AddAsync(range);
            await _db.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceEdited, "InitialBalance", balanceId,
                entityName: balance.Name,
                details: $"إضافة نطاق {request.PieceCount:N0} قطعة");

            var fromStage = orderedStages.First(s => s.Id == request.FromStageId);
            var toStage = orderedStages.First(s => s.Id == request.ToStageId);

            return new InitialBalanceRangeDto
            {
                Id = range.Id,
                FromStageId = fromStage.Id,
                FromStageName = fromStage.StageName,
                ToStageId = toStage.Id,
                ToStageName = toStage.StageName,
                PieceCount = range.PieceCount,
                SortOrder = range.SortOrder
            };
        }

        /// <summary>يشيل نطاقًا من رصيد — الاستخدامات المرتبطة به (لو موجودة) تفضل قايمة، الرابط بس بيروح (SetNull)</summary>
        public async Task RemoveRangeAsync(int rangeId)
        {
            var range = await _db.InitialBalanceRanges.FindAsync(rangeId)
                ?? throw new InvalidOperationException("النطاق غير موجود");

            var balanceId = range.InitialBalanceId;
            _db.InitialBalanceRanges.Remove(range);
            await _db.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceEdited, "InitialBalance", balanceId,
                details: $"حذف نطاق {range.PieceCount:N0} قطعة");
        }

        // ======================= التعديل والحذف =======================

        /// <summary>تعديل الاسم/الملاحظات — الكمية مايتغيرش بعد أي استخدام (شوف DeleteAsync لنفس المنطق)</summary>
        public async Task<InitialBalanceDto> UpdateAsync(
            int balanceId, string name, string? notes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم الرصيد مطلوب", nameof(name));

            var balance = await _db.InitialBalances
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            balance.Name = name.Trim();
            balance.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            await _db.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceEdited, "InitialBalance", balanceId,
                entityName: balance.Name, details: "تعديل بيانات الرصيد");

            return await GetByIdAsync(balanceId)
                ?? throw new InvalidOperationException("تعذّر تحميل الرصيد بعد التعديل");
        }

        /// <summary>
        /// حذف ناعم لرصيد أولي — **مرفوض لو استُخدم منه أي قطعة**: الاستخدام
        /// مرتبط بيومية/أجر عامل حقيقي، وحذف الرصيد وقتها كان هيسيب
        /// الاستخدام معلّق بلا مصدر (شوف قاعدة "مفيش حذف تسلسلي غير آمن"
        /// في مواصفات الفيتشر). لازم تراجع/تشيل الاستخدامات الأول.
        /// </summary>
        public async Task DeleteAsync(int balanceId, string? deletedBy, string? reason)
        {
            var balance = await _db.InitialBalances
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            if (balance.UsedQuantity > 0)
                throw new InvalidOperationException(
                    "الرصيد ده اتاخد منه جزء بالفعل (مرتبط بأجر عامل حقيقي) — راجع الاستخدامات الأول قبل الحذف");

            balance.IsDeleted = true;
            balance.DeletedAt = DateTime.Now;
            balance.DeletedBy = deletedBy;
            balance.DeletionReason = reason;
            balance.DeletedName = balance.Name;

            await _db.SaveChangesAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceDeleted, "InitialBalance", balanceId,
                entityName: balance.Name, reason: reason,
                details: $"{balance.Quantity:N0} قطعة");
        }

        // ======================= السحب/الإكمال =======================

        /// <summary>
        /// يسحب من رصيد أولي — إكمال جزئي أو كلي لواحد أو أكتر من نطاقاته.
        /// بينادي <see cref="ProductionFlowService.RecordFlowAsync"/> نفسها
        /// (شوف تعليق الكلاس) بحيث السحب رحلة إنتاج عادية بالكامل: نفس
        /// WorkerAssignmentGuard، ونفس رسائل الرفض/التأكيد
        /// (<see cref="AssignmentConfirmationRequiredException"/> بتتصعّد
        /// زي أي رحلة عادية — المرحلتين نفس نمط SaveFlowAsync).
        /// "سحب الكل" = المنادي يبعت كل النطاقات النشطة بكامل المتبقي منها؛
        /// مفيش method منفصلة لها.
        /// </summary>
        public async Task<FlowSaveResultDto> WithdrawAsync(
            int balanceId,
            IReadOnlyList<InitialBalanceRangeWithdrawalDto> rangeWithdrawals,
            IReadOnlyList<FlowShareDto> shares,
            DateTime date,
            bool confirmOverride = false,
            string operationsPassword = "")
        {
            if (rangeWithdrawals.Count == 0)
                throw new InvalidOperationException("اختار نطاق واحد على الأقل للسحب منه");

            var balance = await _db.InitialBalances
                .Include(b => b.Ranges)
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            // مايتحسبش تكمل حاجة قبل ما تتعمل أصلًا
            if (date.Date < balance.OriginalDate)
                throw new InvalidOperationException(
                    $"تاريخ السحب ({date:yyyy/MM/dd}) لازم يكون بعد أو يساوي تاريخ الرصيد الأصلي ({balance.OriginalDate:yyyy/MM/dd})");

            var rangesById = balance.Ranges.ToDictionary(r => r.Id);
            var flowRanges = new List<FlowRangeDto>();
            var rangeIdByFlowIndex = new List<int>();

            foreach (var withdrawal in rangeWithdrawals)
            {
                var range = rangesById.TryGetValue(withdrawal.RangeId, out var r)
                    ? r
                    : throw new InvalidOperationException("النطاق المحدد لا ينتمي لهذا الرصيد");

                if (withdrawal.PieceCount <= 0)
                    throw new InvalidOperationException("عدد القطع المسحوبة يجب أن يكون رقمًا موجبًا");

                var remaining = range.PieceCount - UsedFromRange(balance, range);
                if (withdrawal.PieceCount > remaining)
                    throw new InvalidOperationException(
                        $"الكمية المطلوبة من النطاق ({withdrawal.PieceCount:N0}) أكبر من المتاح فيه ({remaining:N0})");

                flowRanges.Add(new FlowRangeDto
                {
                    FromStageId = range.FromStageId,
                    ToStageId = range.ToStageId,
                    PieceCount = withdrawal.PieceCount
                });
                rangeIdByFlowIndex.Add(range.Id);
            }

            var result = await _productionFlow.RecordFlowAsync(
                balance.ProductId, date, flowRanges, shares,
                confirmOverride: confirmOverride,
                operationsPassword: operationsPassword,
                postWriteHook: rows => WriteUsageRowsAsync(balanceId, date, rangesById, rangeIdByFlowIndex, rows));

            await _log.LogAsync(
                ActivityEventType.InitialBalanceUsed, "InitialBalance", balanceId,
                entityName: balance.Name,
                details: $"{rangeWithdrawals.Sum(w => w.PieceCount):N0} قطعة يوم {date:yyyy/MM/dd}");

            return result;
        }

        /// <summary>
        /// بيتنفذ جوه معاملة RecordFlowAsync نفسها (postWriteHook) بعد ما
        /// DailyProductionId بقى حقيقي — بيسجل InitialBalanceUsage واحد
        /// لكل صف وصل **مرحلة خروج نطاقه** بس (مش كل مرحلة وسيطة)، عشان
        /// InitialBalance.UsedQuantity (وحساب "المتاح" فوق) ما يتضاعفش
        /// لما نطاق بيغطي أكتر من مرحلة.
        /// </summary>
        private async Task WriteUsageRowsAsync(
            int balanceId, DateTime date,
            Dictionary<int, InitialBalanceRange> rangesById, List<int> rangeIdByFlowIndex,
            IReadOnlyList<CreatedProductionRowDto> rows)
        {
            foreach (var row in rows)
            {
                if (row.SubmittedRangeIndex < 0 || row.SubmittedRangeIndex >= rangeIdByFlowIndex.Count)
                    continue; // مش من ضمن نطاقات السحب دي (نظريًا مايحصلش، rangeIndex بيتحسب من نفس الـ ranges اللي بعتناها)

                var range = rangesById[rangeIdByFlowIndex[row.SubmittedRangeIndex]];
                if (row.ProductionStageId != range.ToStageId) continue; // مرحلة وسيطة — إنتاج وأجر حقيقي، بدون تتبع رصيد منفصل

                await _db.InitialBalanceUsages.AddAsync(new InitialBalanceUsage
                {
                    InitialBalanceId = balanceId,
                    InitialBalanceRangeId = range.Id,
                    UsedDate = date.Date,
                    Quantity = row.PieceCount,
                    WorkerId = row.WorkerId,
                    ProductionStageId = row.ProductionStageId,
                    DailyProductionId = row.DailyProductionId
                });
            }
        }

        /// <summary>
        /// كام قطعة اتاخدت فعلًا من نطاق معيّن — **مش** كل استخدام مرتبط
        /// بيه بيتحسب: سحب هالك (<see cref="InitialBalanceUsage.ProductionScrapId"/>
        /// موجود) بيتحسب دايمًا لأنه استهلاك نهائي (القطعة خرجت من الخط
        /// خالص)، لكن سحب إكمال إنتاج (<see cref="InitialBalanceUsage.DailyProductionId"/>
        /// موجود) بيتحسب بس لو وصل **مرحلة خروج النطاق** — غيره صفوف
        /// وسيطة (شوف WriteUsageRowsAsync). لو الحساب اتعمل بشكل مختلف
        /// بين المسارين، سحب هالك من أول مرحلة في نطاق متعدد المراحل
        /// كان هيفلت من الحساب تمامًا ويسمح بسحب أكتر من المتاح الحقيقي.
        /// </summary>
        private static int UsedFromRange(InitialBalance balance, InitialBalanceRange range) =>
            balance.Usages
                .Where(u => u.InitialBalanceRangeId == range.Id)
                .Where(u => u.ProductionScrapId is not null || u.ProductionStageId == range.ToStageId)
                .Sum(u => u.Quantity);

        /// <summary>
        /// يسحب جزء من رصيد أولي ويحوّله لهالك بدل إكمال إنتاج — بنفس
        /// كيان الهالك الموجود (<see cref="ProductionScrap"/>)، مش كيان
        /// جديد. بيستخدم بوابة أمان الهالك نفسها (كلمة سر + رفض يوم
        /// مقفول) اللي ScrapService.RecordAsync بتستخدمها، من غير ما
        /// يكررها.
        /// </summary>
        public async Task<ProductionScrap> WithdrawToScrapAsync(
            int balanceId, int rangeId, int stageId, DateTime date, int pieceCount,
            int? scrapReasonId, string? note, string operationsPassword)
        {
            if (pieceCount <= 0)
                throw new ArgumentException("عدد القطع المحوّلة لهالك يجب أن يكون رقمًا موجبًا", nameof(pieceCount));

            var balance = await _db.InitialBalances
                .Include(b => b.Ranges)
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            var range = balance.Ranges.FirstOrDefault(r => r.Id == rangeId)
                ?? throw new InvalidOperationException("النطاق المحدد لا ينتمي لهذا الرصيد");

            if (date.Date < balance.OriginalDate)
                throw new InvalidOperationException(
                    $"تاريخ التحويل لهالك ({date:yyyy/MM/dd}) لازم يكون بعد أو يساوي تاريخ الرصيد الأصلي ({balance.OriginalDate:yyyy/MM/dd})");

            // تقدر تحوّل بس من المرحلة اللي القطع واقفة فيها فعلًا —
            // مش من أي مرحلة تانية في النطاق
            if (stageId != range.FromStageId)
                throw new InvalidOperationException("التحويل لهالك لازم يكون من المرحلة اللي الرصيد واقف فيها بالظبط");

            var remaining = range.PieceCount - UsedFromRange(balance, range);
            if (pieceCount > remaining)
                throw new InvalidOperationException(
                    $"الكمية المطلوب تحويلها لهالك ({pieceCount:N0}) أكبر من المتاح في النطاق ({remaining:N0})");

            await _scrap.EnsureAllowedAsync(date, operationsPassword);

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            var scrap = await _scrap.RecordCoreAsync(stageId, date, pieceCount, scrapReasonId, note);

            await _db.InitialBalanceUsages.AddAsync(new InitialBalanceUsage
            {
                InitialBalanceId = balanceId,
                InitialBalanceRangeId = range.Id,
                UsedDate = date.Date,
                Quantity = pieceCount,
                ProductionStageId = stageId,
                ProductionScrapId = scrap.Id,
                Notes = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _log.LogAsync(
                ActivityEventType.InitialBalanceUsed, "InitialBalance", balanceId,
                entityName: balance.Name,
                details: $"{pieceCount:N0} قطعة تحويل لهالك يوم {date:yyyy/MM/dd}");

            return scrap;
        }

        // ======================= القراية =======================

        /// <summary>كل الأرصدة الأولية لمنتج معين — لبطاقة الرصيد الأولي في شاشة الإنتاج اليومي</summary>
        public async Task<IReadOnlyList<InitialBalanceDto>> GetForProductAsync(int productId)
        {
            var balances = await _db.InitialBalances
                .AsNoTracking()
                .Include(b => b.Product)
                .Include(b => b.Ranges).ThenInclude(r => r.FromStage)
                .Include(b => b.Ranges).ThenInclude(r => r.ToStage)
                .Include(b => b.Usages)
                .Where(b => b.ProductId == productId)
                .OrderByDescending(b => b.OriginalDate).ThenByDescending(b => b.CreatedAt)
                .ToListAsync();

            return balances.Select(ToDto).ToList();
        }

        /// <summary>
        /// تجميع كل أرصدة منتج في رقم واحد — للكارت المُجمّع (progress bar)
        /// في شاشة الإنتاج اليومي بدل عرض كل رصيد لوحده. عرض بصري بحت:
        /// بيبني فوق نفس GetForProductAsync، البيانات الأصلية مش بتتغيّر.
        /// </summary>
        public async Task<InitialBalanceSummaryDto> GetProductSummaryAsync(int productId)
        {
            var balances = await GetForProductAsync(productId);

            return new InitialBalanceSummaryDto
            {
                ProductId = productId,
                TotalQuantity = balances.Sum(b => b.Quantity),
                UsedQuantity = balances.Sum(b => b.UsedQuantity),
                RemainingQuantity = balances.Sum(b => b.RemainingQuantity),
                ActiveBalanceCount = balances.Count(b => b.Status != InitialBalanceStatus.Completed)
            };
        }

        public async Task<InitialBalanceDto?> GetByIdAsync(int balanceId)
        {
            var balance = await _db.InitialBalances
                .AsNoTracking()
                .Include(b => b.Product)
                .Include(b => b.Ranges).ThenInclude(r => r.FromStage)
                .Include(b => b.Ranges).ThenInclude(r => r.ToStage)
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId);

            return balance is null ? null : ToDto(balance);
        }

        /// <summary>تاريخ استخدامات رصيد واحد — للتتبع الكامل (Created → ... → Used → Completed)</summary>
        public async Task<IReadOnlyList<InitialBalanceUsageDto>> GetHistoryAsync(int balanceId)
        {
            return await _db.InitialBalanceUsages
                .AsNoTracking()
                .Include(u => u.Worker)
                .Include(u => u.ProductionStage)
                .Where(u => u.InitialBalanceId == balanceId)
                .OrderBy(u => u.UsedDate).ThenBy(u => u.CreatedAt)
                .Select(u => new InitialBalanceUsageDto
                {
                    Id = u.Id,
                    UsedDate = u.UsedDate,
                    Quantity = u.Quantity,
                    WorkerId = u.WorkerId.GetValueOrDefault(),
                    WorkerName = u.Worker == null ? string.Empty : u.Worker.FullName,
                    ProductionStageId = u.ProductionStageId,
                    StageName = u.ProductionStage.StageName,
                    InitialBalanceRangeId = u.InitialBalanceRangeId,
                    Notes = u.Notes,
                    RecordedBy = u.RecordedBy,
                    CreatedAt = u.CreatedAt
                })
                .ToListAsync();
        }

        private static InitialBalanceDto ToDto(InitialBalance b) => new()
        {
            Id = b.Id,
            ProductId = b.ProductId,
            ProductName = b.Product.Name,
            Name = b.Name,
            Notes = b.Notes,
            Quantity = b.Quantity,
            UsedQuantity = b.UsedQuantity,
            RemainingQuantity = b.RemainingQuantity,
            Status = b.Status,
            OriginalDate = b.OriginalDate,
            Source = b.Source,
            OriginalDailyProductionId = b.OriginalDailyProductionId,
            CreatedAt = b.CreatedAt,
            CreatedBy = b.CreatedBy,
            Ranges = b.Ranges
                .OrderBy(r => r.SortOrder)
                .Select(r => new InitialBalanceRangeDto
                {
                    Id = r.Id,
                    FromStageId = r.FromStageId,
                    FromStageName = r.FromStage.StageName,
                    ToStageId = r.ToStageId,
                    ToStageName = r.ToStage.StageName,
                    PieceCount = r.PieceCount,
                    SortOrder = r.SortOrder
                })
                .ToList()
        };
    }
}
