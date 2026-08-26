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
    /// وتسجيل استخدام/إكمال.
    ///
    /// **القاعدة الأهم اللي كل منطق الاستخدام هنا مبني عليها**: إكمال
    /// جزء من الرصيد بيتحسب في يومية/أجر العامل بتاريخ الإكمال الفعلي
    /// (سجل <see cref="DailyProduction"/> جديد بعلم
    /// <see cref="DailyProduction.IsBalanceCompletion"/>)، لكن الإنتاج
    /// الفعلي الحقيقي للمرحلة بيتسجّل بتاريخ الإنتاج **الأصلي** لصاحب
    /// الرصيد على <see cref="ProductionStageOutput"/> — عشان القطع دي
    /// تفضل محسوبة على تاريخها الأصلي مش تتحسب إنتاج جديد يوم الإكمال
    /// (شوف <see cref="ProductionStageOutputService"/>).
    /// </summary>
    public class InitialBalanceService
    {
        private readonly AppDbContext _db;
        private readonly IUnitOfWork _unitOfWork;
        private readonly OperationsPasswordService _gate;
        private readonly ActivityLogService _log;
        private readonly ProductionStageOutputService _productionOutput;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IWorkerSkillRepository _workerSkillRepo;

        public InitialBalanceService(
            AppDbContext db,
            IUnitOfWork unitOfWork,
            OperationsPasswordService gate,
            ActivityLogService log,
            ProductionStageOutputService productionOutput,
            IAttendanceRepository attendanceRepo,
            IProductionDayClosureRepository closureRepo,
            IWorkerSkillRepository workerSkillRepo)
        {
            _db = db;
            _unitOfWork = unitOfWork;
            _gate = gate;
            _log = log;
            _productionOutput = productionOutput;
            _attendanceRepo = attendanceRepo;
            _closureRepo = closureRepo;
            _workerSkillRepo = workerSkillRepo;
        }

        // ======================= الإنشاء =======================

        /// <summary>ينشئ رصيدًا أوليًا جديدًا (يدويًا أو من قطع ناقصة برحلة إنتاج)، مع نطاقاته الابتدائية لو موجودة</summary>
        public async Task<InitialBalanceDto> CreateAsync(CreateInitialBalanceRequest request, string? createdBy = null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("اسم الرصيد مطلوب", nameof(request));
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ArgumentException("سبب الرصيد مطلوب", nameof(request));
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
                Reason = request.Reason.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                Quantity = request.Quantity,
                OriginalDate = request.OriginalDate.Date,
                Source = request.Source,
                OriginalDailyProductionId = request.OriginalDailyProductionId,
                CreatedBy = createdBy
            };

            var rangesTotal = 0;
            foreach (var rangeReq in request.Ranges)
            {
                ValidateRangeStages(orderedStages, rangeReq.FromStageId, rangeReq.ToStageId, rangeReq.PieceCount);
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
                details: $"{balance.Quantity:N0} قطعة من تاريخ {balance.OriginalDate:yyyy/MM/dd} — {balance.Reason}");

            return await GetByIdAsync(balance.Id)
                ?? throw new InvalidOperationException("تعذّر إنشاء الرصيد الأولي");
        }

        /// <summary>
        /// يتحقق إن مرحلتي النطاق تابعتين لخط إنتاج المنتج وبترتيب صحيح
        /// (من الأسبق للأحدث) — نفس قاعدة نطاقات رحلة الإنتاج العادية
        /// بالظبط (شوف <see cref="ProductionFlowService.RecordFlowAsync"/>).
        /// </summary>
        private static void ValidateRangeStages(
            List<ProductionStage> orderedStages, int fromStageId, int toStageId, int pieceCount)
        {
            if (pieceCount <= 0)
                throw new InvalidOperationException("عدد قطع النطاق يجب أن يكون رقمًا موجبًا");

            var fromIndex = orderedStages.FindIndex(s => s.Id == fromStageId);
            var toIndex = orderedStages.FindIndex(s => s.Id == toStageId);

            if (fromIndex < 0 || toIndex < 0)
                throw new InvalidOperationException("النطاق بيشاور على مرحلة مش من مراحل خط الإنتاج النشطة لهذا المنتج");

            if (fromIndex > toIndex)
                throw new InvalidOperationException(
                    $"النطاق معكوس: \"{orderedStages[fromIndex].StageName}\" بتيجي بعد " +
                    $"\"{orderedStages[toIndex].StageName}\" في خط الإنتاج");
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
            ValidateRangeStages(orderedStages, request.FromStageId, request.ToStageId, request.PieceCount);

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

        /// <summary>تعديل الاسم/السبب/الملاحظات — الكمية مايتغيرش بعد أي استخدام (شوف DeleteAsync لنفس المنطق)</summary>
        public async Task<InitialBalanceDto> UpdateAsync(
            int balanceId, string name, string reason, string? notes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم الرصيد مطلوب", nameof(name));
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب الرصيد مطلوب", nameof(reason));

            var balance = await _db.InitialBalances
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == balanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            balance.Name = name.Trim();
            balance.Reason = reason.Trim();
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

        // ======================= الاستخدام/الإكمال =======================

        /// <summary>
        /// يسجل استخدام/إكمال جزء من رصيد أولي: بيتحسب في يومية/أجر
        /// العامل بتاريخ <see cref="RecordInitialBalanceUsageRequest.UsedDate"/>،
        /// وبيتسجل الإنتاج الفعلي على تاريخ الرصيد الأصلي — شوف تعليق
        /// الكلاس. لو النطاق (<see cref="RecordInitialBalanceUsageRequest.InitialBalanceRangeId"/>)
        /// محدد، الإنتاج الفعلي بيتسجل على **كل مراحل النطاق** (زي أي
        /// نطاق في رحلة إنتاج عادية)، مش المرحلة اللي العامل اتحاسب
        /// عليها بس.
        /// </summary>
        public async Task<InitialBalanceUsageDto> RecordUsageAsync(
            RecordInitialBalanceUsageRequest request, string? recordedBy = null)
        {
            if (request.Quantity <= 0)
                throw new ArgumentException("عدد القطع المستخدمة يجب أن يكون رقمًا موجبًا", nameof(request));

            var gate = await _gate.VerifyAsync(SensitiveAction.RecordProduction, request.OperationsPassword);
            if (!gate.IsAllowed)
                throw new InvalidOperationException(gate.Message);

            var balance = await _db.InitialBalances
                .Include(b => b.Product).ThenInclude(p => p.Stages)
                .Include(b => b.Ranges)
                .Include(b => b.Usages)
                .FirstOrDefaultAsync(b => b.Id == request.InitialBalanceId)
                ?? throw new InvalidOperationException("الرصيد الأولي غير موجود");

            if (request.Quantity > balance.RemainingQuantity)
                throw new InvalidOperationException(
                    $"الكمية المطلوبة ({request.Quantity:N0}) أكبر من المتاح في الرصيد ({balance.RemainingQuantity:N0})");

            if (await _closureRepo.IsClosedAsync(request.UsedDate))
                throw new InvalidOperationException(DayClosureService.ClosedDayMessage(request.UsedDate));

            var orderedStages = ProductionLine.Active(balance.Product);
            var stage = orderedStages.FirstOrDefault(s => s.Id == request.ProductionStageId)
                ?? throw new InvalidOperationException("المرحلة المحددة مش من مراحل خط إنتاج هذا المنتج");

            InitialBalanceRange? range = null;
            var outputStageIds = new List<int> { stage.Id };

            if (request.InitialBalanceRangeId is { } rangeId)
            {
                range = balance.Ranges.FirstOrDefault(r => r.Id == rangeId)
                    ?? throw new InvalidOperationException("النطاق المحدد لا ينتمي لهذا الرصيد");

                var fromIndex = orderedStages.FindIndex(s => s.Id == range.FromStageId);
                var toIndex = orderedStages.FindIndex(s => s.Id == range.ToStageId);
                var stageIndex = orderedStages.FindIndex(s => s.Id == stage.Id);

                if (stageIndex < fromIndex || stageIndex > toIndex)
                    throw new InvalidOperationException("المرحلة المحددة برّه نطاق الرصيد المختار");

                // الإنتاج الفعلي بيتسجل على كل مراحل النطاق، زي أي نطاق في
                // رحلة إنتاج عادية — القطعة اللي وصلت لآخر مرحلة في النطاق
                // تكون عدّت على كل اللي قبلها فيه
                outputStageIds = orderedStages.Skip(fromIndex).Take(toIndex - fromIndex + 1).Select(s => s.Id).ToList();
            }

            // العامل لازم يكون مؤهل لهذه المرحلة فعلًا — نفس شرط أي تسجيل إنتاج عادي
            _ = await _workerSkillRepo.GetAsync(request.WorkerId, request.ProductionStageId)
                ?? throw new InvalidOperationException("العامل المحدد غير مؤهل لهذه المرحلة");

            var production = new DailyProduction
            {
                WorkerId = request.WorkerId,
                ProductionStageId = request.ProductionStageId,
                Date = request.UsedDate.Date,
                PieceCount = request.Quantity,
                PiecesPerWorkdayAtEntry = stage.PiecesPerWorkday,
                IsBalanceCompletion = true
            };

            var usage = new InitialBalanceUsage
            {
                InitialBalanceId = balance.Id,
                InitialBalanceRangeId = range?.Id,
                UsedDate = request.UsedDate.Date,
                Quantity = request.Quantity,
                WorkerId = request.WorkerId,
                ProductionStageId = request.ProductionStageId,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                RecordedBy = recordedBy,
                DailyProduction = production // FK بيتحل تلقائيًا وقت الحفظ (نفس السجل جوه نفس الـ Context)
            };

            await using (var transaction = await _unitOfWork.BeginWriteTransactionAsync())
            {
                await _db.DailyProductions.AddAsync(production);
                await _db.InitialBalanceUsages.AddAsync(usage);

                // الإنتاج الفعلي الحقيقي بيتسجل بتاريخ الرصيد الأصلي، مش
                // تاريخ الإكمال — شوف تعليق الكلاس
                foreach (var stageId in outputStageIds)
                    await _productionOutput.RecordOutputAsync(stageId, balance.OriginalDate, request.Quantity);

                // حضور تلقائي للعامل يوم الإكمال لو مالوش سجل حضور بالفعل
                if (await _attendanceRepo.GetByWorkerAndDateAsync(request.WorkerId, request.UsedDate) is null)
                {
                    await _attendanceRepo.AddAsync(new Attendance
                    {
                        WorkerId = request.WorkerId,
                        Date = request.UsedDate.Date,
                        Status = AttendanceStatus.Present
                    });
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await _log.LogAsync(
                ActivityEventType.InitialBalanceUsed, "InitialBalance", balance.Id,
                entityName: balance.Name,
                details: $"{request.Quantity:N0} قطعة يوم {request.UsedDate:yyyy/MM/dd} — عامل #{request.WorkerId}");

            return new InitialBalanceUsageDto
            {
                Id = usage.Id,
                UsedDate = usage.UsedDate,
                Quantity = usage.Quantity,
                WorkerId = usage.WorkerId,
                WorkerName = (await _db.Workers.FindAsync(usage.WorkerId))?.FullName ?? string.Empty,
                ProductionStageId = usage.ProductionStageId,
                StageName = stage.StageName,
                InitialBalanceRangeId = usage.InitialBalanceRangeId,
                Notes = usage.Notes,
                RecordedBy = usage.RecordedBy,
                CreatedAt = usage.CreatedAt
            };
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
                    WorkerId = u.WorkerId,
                    WorkerName = u.Worker.FullName,
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
            Reason = b.Reason,
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
