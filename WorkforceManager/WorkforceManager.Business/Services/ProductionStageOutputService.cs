using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الإنتاج الفعلي الحقيقي لمرحلة في يوم — منفصل عن قطع العمال، شوف
    /// <see cref="ProductionStageOutput"/>.
    ///
    /// **المكان الوحيد اللي بيكتب الرقم ده.** بيتكتب من
    /// <see cref="ProductionFlowService.RecordFlowAsync"/> — كل مرحلة في
    /// نطاق بتاخد نفس رقم النطاق، زي ما كانت بتتحقق منه بس من غير تخزين.
    ///
    /// **تراكمي زي الهالك، مش استبدال**: <see cref="RecordOutputAsync"/>
    /// بيجمع على القيمة الموجودة مش يستبدلها — رحلتان في نفس اليوم على
    /// نفس المرحلة (مثلاً بعد إعادة فتح يوم مقفول وتسجيل زيادة) لازم
    /// يجمعوا، زي ما بالضبط <see cref="DailyProduction"/> بتتجمّع من عدة
    /// عمال على نفس المرحلة/اليوم. استبدال هنا كان يمسح الرحلة الأولى.
    ///
    /// **القراية بترجع لحساب مجموع العمال القديم تلقائيًا** لأي مرحلة/يوم
    /// **مالوش سجل في الجدول ده** — الجدول بدأ فاضي، وأيام قبل هذا
    /// الفيتشر مالها رقم إنتاج فعلي محفوظ. من غير الرجوع ده، كل تقرير
    /// قديم كان هيطلع صفر بدل رقمه الحقيقي.
    /// </summary>
    public class ProductionStageOutputService
    {
        private readonly AppDbContext _db;

        public ProductionStageOutputService(AppDbContext db)
        {
            _db = db;
        }

        // ======================= التسجيل =======================

        /// <summary>
        /// بيسجّل إنتاج فعلي على مرحلة في يوم — **تراكمي**، بيتجمّع مع
        /// أي رقم مسجّل قبله لنفس المرحلة/اليوم. مبيناديش SaveChangesAsync
        /// — بيتحفظ مع باقي الرحلة في حفظة <see cref="ProductionFlowService"/>
        /// الواحدة (بيشارك نفس AppDbContext عن طريق الحقن في الـ constructor).
        /// </summary>
        public async Task RecordOutputAsync(int productionStageId, DateTime date, int pieceCount)
        {
            if (pieceCount <= 0) return;

            var day = date.Date;

            var existing = await _db.ProductionStageOutputs.FirstOrDefaultAsync(o =>
                o.ProductionStageId == productionStageId && o.Date == day);

            if (existing is not null)
            {
                existing.PieceCount += pieceCount;
                return;
            }

            await _db.ProductionStageOutputs.AddAsync(new ProductionStageOutput
            {
                ProductionStageId = productionStageId,
                Date = day,
                PieceCount = pieceCount
            });
        }

        public Task<bool> HasAnyForStageAsync(int stageId) =>
            _db.ProductionStageOutputs.AnyAsync(o => o.ProductionStageId == stageId);

        // ======================= القراية =======================

        /// <summary>إنتاج فعلي ليوم واحد لكل مرحلة — رقم جديد لو موجود، وإلا مجموع العمال القديم</summary>
        public async Task<IReadOnlyDictionary<int, int>> GetStageTotalsOnAsync(DateTime date)
        {
            var day = date.Date;

            var totals = await _db.ProductionStageOutputs
                .AsNoTracking()
                .Where(o => o.Date == day)
                .GroupBy(o => o.ProductionStageId)
                .Select(g => new { StageId = g.Key, Pieces = g.Sum(o => o.PieceCount) })
                .ToDictionaryAsync(r => r.StageId, r => r.Pieces);

            var legacy = await _db.DailyProductions
                .AsNoTracking()
                .Where(dp => dp.Date == day)
                .GroupBy(dp => dp.ProductionStageId)
                .Select(g => new { StageId = g.Key, Pieces = g.Sum(dp => dp.PieceCount) })
                .ToListAsync();

            var merged = new Dictionary<int, int>(totals);
            foreach (var row in legacy)
                if (!merged.ContainsKey(row.StageId))
                    merged[row.StageId] = row.Pieces;

            return merged;
        }

        /// <summary>
        /// إجمالي الإنتاج الفعلي لكل مرحلة لحد اليوم ده — نفس شكل حساب
        /// الشغل الواقف. لكل (مرحلة، يوم) لسه من غير رقم جديد، بيتجمع
        /// مجموع عمالها القديم على الإجمالي — بدون هذا الدمج بالتاريخ،
        /// يوم مسجَّل جزئيًا بالطريقة الجديدة وباقيه بالقديمة كان يُعَدّ مرتين.
        /// </summary>
        public async Task<IReadOnlyDictionary<int, int>> GetStageTotalsUpToAsync(DateTime date)
        {
            var day = date.Date;

            var newRows = await _db.ProductionStageOutputs
                .AsNoTracking()
                .Where(o => o.Date <= day)
                .Select(o => new { o.ProductionStageId, o.Date, o.PieceCount })
                .ToListAsync();

            if (newRows.Count == 0)
            {
                // مسار سريع: مفيش ولا سجل جديد لحد اليوم ده — نفس الحساب
                // القديم بالظبط، من غير أي تكلفة دمج زيادة
                var legacyOnly = await _db.DailyProductions
                    .AsNoTracking()
                    .Where(dp => dp.Date <= day)
                    .GroupBy(dp => dp.ProductionStageId)
                    .Select(g => new { StageId = g.Key, Pieces = g.Sum(dp => dp.PieceCount) })
                    .ToListAsync();

                return legacyOnly.ToDictionary(r => r.StageId, r => r.Pieces);
            }

            var covered = newRows.Select(r => (r.ProductionStageId, r.Date)).ToHashSet();
            var totals = newRows
                .GroupBy(r => r.ProductionStageId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.PieceCount));

            var legacyByStageDate = await _db.DailyProductions
                .AsNoTracking()
                .Where(dp => dp.Date <= day)
                .GroupBy(dp => new { dp.ProductionStageId, dp.Date })
                .Select(g => new { g.Key.ProductionStageId, g.Key.Date, Pieces = g.Sum(dp => dp.PieceCount) })
                .ToListAsync();

            foreach (var row in legacyByStageDate)
            {
                if (covered.Contains((row.ProductionStageId, row.Date))) continue;
                totals[row.ProductionStageId] = totals.GetValueOrDefault(row.ProductionStageId) + row.Pieces;
            }

            return totals;
        }

        /// <summary>صفوف (منتج، مرحلة، تاريخ، قطع) لمدة كاملة — للتبويب باليوم/الأسبوع/الشهر</summary>
        public async Task<IReadOnlyList<ProductionOutputRecordDto>> GetByRangeAsync(DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate = to.Date;

            var newRows = await _db.ProductionStageOutputs
                .AsNoTracking()
                .Include(o => o.ProductionStage).ThenInclude(s => s.Product)
                .Where(o => o.Date >= fromDate && o.Date <= toDate)
                .Select(o => new ProductionOutputRecordDto
                {
                    Date = o.Date,
                    ProductId = o.ProductionStage.ProductId,
                    ProductName = o.ProductionStage.Product.Name,
                    ProductionStageId = o.ProductionStageId,
                    StageName = o.ProductionStage.StageName,
                    PieceCount = o.PieceCount
                })
                .ToListAsync();

            var covered = newRows.Select(r => (r.ProductionStageId, r.Date)).ToHashSet();

            var legacyByStageDate = await _db.DailyProductions
                .AsNoTracking()
                .Include(dp => dp.ProductionStage).ThenInclude(s => s.Product)
                .Where(dp => dp.Date >= fromDate && dp.Date <= toDate)
                .GroupBy(dp => new { dp.ProductionStageId, dp.Date })
                .Select(g => new
                {
                    g.Key.ProductionStageId,
                    g.Key.Date,
                    Pieces = g.Sum(dp => dp.PieceCount),
                    ProductId = g.First().ProductionStage.ProductId,
                    ProductName = g.First().ProductionStage.Product.Name,
                    StageName = g.First().ProductionStage.StageName
                })
                .ToListAsync();

            var legacyRows = legacyByStageDate
                .Where(r => !covered.Contains((r.ProductionStageId, r.Date)))
                .Select(r => new ProductionOutputRecordDto
                {
                    Date = r.Date,
                    ProductId = r.ProductId,
                    ProductName = r.ProductName,
                    ProductionStageId = r.ProductionStageId,
                    StageName = r.StageName,
                    PieceCount = r.Pieces
                });

            return newRows.Concat(legacyRows).ToList();
        }
    }
}
