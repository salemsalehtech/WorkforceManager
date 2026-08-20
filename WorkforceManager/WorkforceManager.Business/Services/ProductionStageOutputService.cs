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

        /// <summary>
        /// بتتنادى فورًا بعد ما DailyProduction يتشال (سجل أو يوم كامل).
        /// لو ده كان **آخر** سجل باقي لنفس (المرحلة، اليوم)، الإنتاج الفعلي
        /// المرتبط بيهم (لو موجود) بقى شبح — مفيش أي سجل عامل وراه خالص —
        /// فبيتشال معاه.
        ///
        /// **لو لسه فيه سجلات تانية باقية لنفس المرحلة/اليوم (لعامل تاني،
        /// أو حتى نفس العامل)، الإنتاج الفعلي مايتلمسش خالص**: الرقم ده
        /// مش مشتق من مجموع قطع العمال — هو رقم النطاق المستقل وقت
        /// التسجيل (شوف تعليق النموذج + اختبار
        /// TheActualOutputNumber_IsStoredIndependently_...)، فحذف أو تعديل
        /// سجل عامل واحد لا يعني إن الإنتاج الفعلي "نقص" أو "زاد" بنفس
        /// القدر — التخمين هنا أخطر من عدم اللمس.
        ///
        /// لازم يتنادى **بعد** ما حذف DailyProduction يتحفظ فعليًا
        /// (SaveChangesAsync) — فحص "فيه سجل باقي؟" بيقرا من قاعدة البيانات،
        /// ولو اتنادى قبل الحفظ هيلاقي السجل المحذوف لسه شايفه موجود.
        /// </summary>
        public async Task RemoveIfNowOrphanedAsync(int productionStageId, DateTime date)
        {
            var day = date.Date;

            var stillHasSupport = await _db.DailyProductions.AnyAsync(dp =>
                dp.ProductionStageId == productionStageId && dp.Date == day);
            if (stillHasSupport) return;

            var existing = await _db.ProductionStageOutputs.FirstOrDefaultAsync(o =>
                o.ProductionStageId == productionStageId && o.Date == day);
            if (existing is not null) _db.ProductionStageOutputs.Remove(existing);
        }

        /// <summary>
        /// تصحيح لمرة واحدة (بينادى من App.OnStartup، بنفس نمط
        /// DeletedRowsCleaner): بيمسح بس صفوف ProductionStageOutput اللي
        /// بقت شبح كامل 100% — صفر سجل DailyProduction باقي خالص لنفس
        /// (المرحلة، اليوم)، يعني كل قطع العمال المرتبطة اتمسحت بالكامل.
        /// أي صف لسه ليه دعم جزئي (حتى باختلاف رقمي عن مجموع العمال)
        /// متتلمسش — الاختلاف ده مصمّم عمدًا (شوف تعليق النموذج)، ومينفعش
        /// نخمّن هل هو عطل أو مقصود. آمن يتنادى أي عدد مرات.
        /// </summary>
        public async Task<int> RemoveFullyOrphanedRowsAsync()
        {
            var outputs = await _db.ProductionStageOutputs
                .Select(o => new { o.Id, o.ProductionStageId, o.Date })
                .ToListAsync();
            if (outputs.Count == 0) return 0;

            var stillSupported = (await _db.DailyProductions
                    .Select(dp => new { dp.ProductionStageId, dp.Date })
                    .Distinct()
                    .ToListAsync())
                .Select(x => (x.ProductionStageId, x.Date))
                .ToHashSet();

            var orphanIds = outputs
                .Where(o => !stillSupported.Contains((o.ProductionStageId, o.Date)))
                .Select(o => o.Id)
                .ToList();
            if (orphanIds.Count == 0) return 0;

            return await _db.ProductionStageOutputs
                .Where(o => orphanIds.Contains(o.Id))
                .ExecuteDeleteAsync();
        }

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
