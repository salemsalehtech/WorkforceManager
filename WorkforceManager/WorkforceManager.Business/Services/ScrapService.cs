using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الهالك: القطع اللي اتشالت من الخط ومش هتتكمّل.
    ///
    /// **المكان الوحيد اللي بيقرا ويكتب الهالك.** الشغل الواقف والإنتاج
    /// التام الاتنين بيطرحوا منه، فلو الرقم اتحسب من مكانين هيبقى فيه
    /// شاشتين بيقولوا رقمين لنفس اليوم.
    ///
    /// المرحلة على السجل معناها **آخر مرحلة القطعة خلصتها قبل ما
    /// تتشال** — والتعريف ده هو اللي بيخلي قاعدة واحدة تغطي الهالك في
    /// نص الخط والهالك بعد آخر مرحلة (شوف <see cref="ProductionScrap"/>).
    /// </summary>
    public class ScrapService
    {
        private readonly AppDbContext _db;
        private readonly ActivityLogService _log;

        public ScrapService(AppDbContext db, ActivityLogService log)
        {
            _db = db;
            _log = log;
        }

        // ======================= التسجيل =======================

        /// <summary>
        /// يسجّل هالك على مرحلة في يوم معين.
        ///
        /// **بيتجمّع مع اللي قبله في نفس اليوم/المرحلة/السبب** بدل ما
        /// يعمل سجل جديد كل مرة: المستخدم اللي سجّل 300 ونسي 200 عايز
        /// يشوف 500 في الآخر، مش سطرين لازم يجمعهم بنفسه.
        /// </summary>
        public async Task<ProductionScrap> RecordAsync(
            int productionStageId, DateTime date, int pieceCount,
            int? reasonId = null, string? note = null, string? recordedBy = null)
        {
            if (pieceCount <= 0)
                throw new ArgumentException("عدد قطع الهالك لازم يكون أكبر من صفر", nameof(pieceCount));

            var day = date.Date;

            var existing = await _db.ProductionScraps.FirstOrDefaultAsync(s =>
                s.ProductionStageId == productionStageId &&
                s.Date == day &&
                s.ScrapReasonId == reasonId);

            if (existing is not null)
            {
                existing.PieceCount += pieceCount;

                // الملاحظتين بيتجمعوا: اللي كتب سببين مختلفين في يوم
                // واحد عايزهم الاتنين، مش الأخير بس
                if (!string.IsNullOrWhiteSpace(note))
                    existing.Note = string.IsNullOrWhiteSpace(existing.Note)
                        ? note.Trim()
                        : $"{existing.Note} — {note.Trim()}";

                await _db.SaveChangesAsync();
                await LogScrapAsync(existing, pieceCount, note);
                return existing;
            }

            var record = new ProductionScrap
            {
                ProductionStageId = productionStageId,
                Date = day,
                PieceCount = pieceCount,
                ScrapReasonId = reasonId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                RecordedBy = recordedBy
            };

            await _db.ProductionScraps.AddAsync(record);
            await _db.SaveChangesAsync();
            await LogScrapAsync(record, pieceCount, note);
            return record;
        }

        /// <summary>
        /// الهالك حركة ليها قيمة: قطع خرجت من الخط ومش هتكمّل، والعمال
        /// اللي بعد المرحلة دي مش هيشوفوها. بيتسجّل **العدد اللي اتضاف
        /// دلوقتي** مش الإجمالي التراكمي — السجل بيحكي اللي حصل، مش
        /// الحالة النهائية.
        /// </summary>
        private async Task LogScrapAsync(ProductionScrap record, int addedPieces, string? note)
        {
            var stage = await _db.ProductionStages
                .Include(s => s.Product)
                .FirstOrDefaultAsync(s => s.Id == record.ProductionStageId);

            await _log.LogAsync(
                ActivityEventType.ScrapRecorded, "ProductionScrap", record.Id,
                entityName: stage is null ? null : $"{stage.Product.Name} — {stage.StageName}",
                reason: note,
                details: $"{addedPieces:N0} قطعة يوم {record.Date:yyyy/MM/dd}");
        }

        public async Task RemoveAsync(int scrapId)
        {
            var record = await _db.ProductionScraps.FindAsync(scrapId)
                ?? throw new InvalidOperationException("سجل الهالك مش موجود");

            _db.ProductionScraps.Remove(record);
            await _db.SaveChangesAsync();
        }

        // ======================= القراية =======================

        /// <summary>هالك يوم واحد بكل تفاصيله (لشاشة التسجيل اليومي)</summary>
        public async Task<IReadOnlyList<ScrapRecordDto>> GetByDateAsync(DateTime date)
        {
            var day = date.Date;

            return await _db.ProductionScraps
                .AsNoTracking()
                .Include(s => s.ProductionStage).ThenInclude(st => st.Product)
                .Include(s => s.Reason)
                .Where(s => s.Date == day)
                .OrderByDescending(s => s.PieceCount)
                .Select(s => new ScrapRecordDto
                {
                    Id = s.Id,
                    Date = s.Date,
                    ProductId = s.ProductionStage.ProductId,
                    ProductName = s.ProductionStage.Product.Name,
                    ProductionStageId = s.ProductionStageId,
                    StageName = s.ProductionStage.StageName,
                    PieceCount = s.PieceCount,
                    ReasonName = s.Reason != null ? s.Reason.Name : null,
                    Note = s.Note
                })
                .ToListAsync();
        }

        /// <summary>هالك مدة كاملة — للتقارير</summary>
        public async Task<IReadOnlyList<ScrapRecordDto>> GetByRangeAsync(DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate = to.Date;

            return await _db.ProductionScraps
                .AsNoTracking()
                .Include(s => s.ProductionStage).ThenInclude(st => st.Product)
                .Include(s => s.Reason)
                .Where(s => s.Date >= fromDate && s.Date <= toDate)
                .OrderBy(s => s.Date)
                .Select(s => new ScrapRecordDto
                {
                    Id = s.Id,
                    Date = s.Date,
                    ProductId = s.ProductionStage.ProductId,
                    ProductName = s.ProductionStage.Product.Name,
                    ProductionStageId = s.ProductionStageId,
                    StageName = s.ProductionStage.StageName,
                    PieceCount = s.PieceCount,
                    ReasonName = s.Reason != null ? s.Reason.Name : null,
                    Note = s.Note
                })
                .ToListAsync();
        }

        /// <summary>
        /// إجمالي الهالك لكل مرحلة لحد اليوم ده — الشكل اللي حساب
        /// الشغل الواقف والإنتاج التام محتاجينه.
        ///
        /// بيتجمّع في الداتابيز مش في الذاكرة، زي مجاميع الإنتاج.
        /// </summary>
        public async Task<IReadOnlyDictionary<int, int>> GetStageTotalsUpToAsync(DateTime date)
        {
            var day = date.Date;

            var rows = await _db.ProductionScraps
                .AsNoTracking()
                .Where(s => s.Date <= day)
                .GroupBy(s => s.ProductionStageId)
                .Select(g => new { StageId = g.Key, Pieces = g.Sum(s => s.PieceCount) })
                .ToListAsync();

            return rows.ToDictionary(r => r.StageId, r => r.Pieces);
        }

        /// <summary>إجمالي الهالك لكل مرحلة في يوم واحد</summary>
        public async Task<IReadOnlyDictionary<int, int>> GetStageTotalsOnAsync(DateTime date)
        {
            var day = date.Date;

            var rows = await _db.ProductionScraps
                .AsNoTracking()
                .Where(s => s.Date == day)
                .GroupBy(s => s.ProductionStageId)
                .Select(g => new { StageId = g.Key, Pieces = g.Sum(s => s.PieceCount) })
                .ToListAsync();

            return rows.ToDictionary(r => r.StageId, r => r.Pieces);
        }

        // ======================= أسباب الهالك =======================

        /// <summary>الأسباب الشغالة بس — دي اللي بتظهر وقت التسجيل</summary>
        public Task<List<ScrapReason>> GetActiveReasonsAsync() =>
            _db.ScrapReasons
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .ToListAsync();

        /// <summary>كل الأسباب (شغالة وموقوفة) — لشاشة الإعدادات</summary>
        public Task<List<ScrapReason>> GetAllReasonsAsync() =>
            _db.ScrapReasons
                .AsNoTracking()
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .ToListAsync();

        public async Task<ScrapReason> AddReasonAsync(string name)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length == 0)
                throw new ArgumentException("اكتب اسم السبب", nameof(name));

            if (await _db.ScrapReasons.AnyAsync(r => r.Name == clean))
                throw new InvalidOperationException($"السبب \"{clean}\" موجود بالفعل");

            var maxOrder = await _db.ScrapReasons.AnyAsync()
                ? await _db.ScrapReasons.MaxAsync(r => r.SortOrder)
                : 0;

            var reason = new ScrapReason { Name = clean, SortOrder = maxOrder + 1, IsActive = true };

            await _db.ScrapReasons.AddAsync(reason);
            await _db.SaveChangesAsync();
            return reason;
        }

        /// <summary>
        /// بيوقف السبب أو بيرجّعه.
        ///
        /// **مفيش حذف** عن قصد: السبب المستخدم في تقارير الشهور اللي
        /// فاتت لازم يفضل معروف. الموقوف بيختفي من التسجيل الجديد بس.
        /// </summary>
        public async Task SetReasonActiveAsync(int reasonId, bool isActive)
        {
            var reason = await _db.ScrapReasons.FindAsync(reasonId)
                ?? throw new InvalidOperationException("السبب مش موجود");

            reason.IsActive = isActive;
            await _db.SaveChangesAsync();
        }

        public async Task RenameReasonAsync(int reasonId, string name)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length == 0)
                throw new ArgumentException("اكتب اسم السبب", nameof(name));

            var reason = await _db.ScrapReasons.FindAsync(reasonId)
                ?? throw new InvalidOperationException("السبب مش موجود");

            if (await _db.ScrapReasons.AnyAsync(r => r.Name == clean && r.Id != reasonId))
                throw new InvalidOperationException($"السبب \"{clean}\" موجود بالفعل");

            reason.Name = clean;
            await _db.SaveChangesAsync();
        }
    }
}
