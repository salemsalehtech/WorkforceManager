using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// ترحيل تلقائي، مرة واحدة بس، لأي "شغل واقف" كان موجود قبل فيتشر
    /// الرصيد الأولي — عشان بعد الترقية كل شغل ناقص يبقى متتبّع كرصيد
    /// أولي بدل ما يفضل رقم تراكمي بس (شوف <see cref="PendingWorkService"/>).
    ///
    /// **بيستخدم نفس حسابات <see cref="PendingWorkService"/> بالظبط
    /// (نفس الاستعلامات) من غير ما يلمسها أو يعدّل فيها** — الخدمة دي
    /// برّه النطاق تمامًا.
    ///
    /// **تقريبي عن قصد، مش دقيق 100%**: "الشغل الواقف" رقم تراكمي بحت
    /// (الفرق بين إجمالي مرحلة والإجمالي اللي قبلها من أول التسجيل)،
    /// **مفيش فيه أي تفصيل تاريخ أو دفعة محفوظ في الأساس** — فمفيش طريقة
    /// نعرف بيه "امتى بالظبط" القطع دي دخلت الخط. الحل المعتمد:
    /// الأرصدة المُرحّلة بتاريخ يوم الترحيل نفسه (مش تاريخ حقيقي مفقود)،
    /// والكمية بس هي اللي محفوظة بدقة.
    /// </summary>
    public class HistoricalPendingMigrationService
    {
        private readonly AppDbContext _db;
        private readonly IProductRepository _products;
        private readonly ProductionStageOutputService _productionOutput;
        private readonly ScrapService _scrap;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ActivityLogService _log;

        public HistoricalPendingMigrationService(
            AppDbContext db,
            IProductRepository products,
            ProductionStageOutputService productionOutput,
            ScrapService scrap,
            IUnitOfWork unitOfWork,
            ActivityLogService log)
        {
            _db = db;
            _products = products;
            _productionOutput = productionOutput;
            _scrap = scrap;
            _unitOfWork = unitOfWork;
            _log = log;
        }

        /// <summary>
        /// بيتنادى من App.OnStartup بعد MigrateAsync مباشرة. آمن يتنادى
        /// أكتر من مرة — الحارس (خطوة 1) بيخليه no-op بعد أول تشغيل ناجح.
        /// </summary>
        public async Task RunOnceAsync()
        {
            // 1) حارس idempotency: لو فيه رصيد مُرحّل بالفعل، الترحيل خلص قبل كده
            if (await _db.InitialBalances.IgnoreQueryFilters().AnyAsync(b => b.Source == InitialBalanceSource.Migrated))
                return;

            var migrationDate = DateTime.Today;

            var products = await _products.GetAllWithStagesAsync();
            var totals = await _productionOutput.GetStageTotalsUpToAsync(migrationDate);
            var scrapTotals = await _scrap.GetStageTotalsUpToAsync(migrationDate);

            int Total(int stageId) => totals.TryGetValue(stageId, out var pieces) ? pieces : 0;
            int Scrap(int stageId) => scrapTotals.TryGetValue(stageId, out var pieces) ? pieces : 0;

            var createdCount = 0;
            var affectedProductIds = new HashSet<int>();

            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            foreach (var product in products)
            {
                // نفس PendingWorkService.Describe بالظبط: خط بمرحلة واحدة
                // مفيهوش "قبل" و"بعد" يتقارنوا
                var line = ProductionLine.Active(product);
                if (line.Count < 2) continue;

                // 2-3) لكل حد فاصل بين مرحلتين متتاليتين
                for (var i = 1; i < line.Count; i++)
                {
                    var before = Total(line[i - 1].Id) - Scrap(line[i - 1].Id);
                    var current = Total(line[i].Id);
                    var pending = before - current;

                    // سالب معناه غلط بيانات (مرحلة اتسجل عليها أكتر من
                    // اللي قبلها) مش شغل واقف — نفس تفسير PendingWorkService،
                    // والترحيل مالوش دعوة بيصلّح غلط بيانات، بس بيتخطاه
                    if (pending <= 0) continue;

                    var balance = new InitialBalance
                    {
                        ProductId = product.Id,
                        Name = $"رصيد مرحلي سابق - {product.Name} - قدام {line[i].StageName}",
                        Quantity = pending,
                        OriginalDate = migrationDate,
                        Source = InitialBalanceSource.Migrated,
                        Notes = "تم إنشاؤه تلقائيًا أثناء ترحيل النظام"
                    };
                    await _db.InitialBalances.AddAsync(balance);

                    // النطاق بيبدأ من line[i] (المرحلة اللي القطع واقفة
                    // قدامها) مش line[i-1] — إنتاج line[i-1] اتسجل خلاص
                    // (هو أصلًا الـ"before" في حساب pending فوق)، فلو
                    // النطاق بدأ منها بردو حسابها هيتكرر لما ده يتسحب
                    await _db.InitialBalanceRanges.AddAsync(new InitialBalanceRange
                    {
                        InitialBalance = balance,
                        FromStageId = line[i].Id,
                        ToStageId = line[^1].Id,
                        PieceCount = pending,
                        SortOrder = 0
                    });

                    createdCount++;
                    affectedProductIds.Add(product.Id);
                }
            }

            if (createdCount > 0)
                await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            if (createdCount > 0)
                await _log.LogAsync(
                    ActivityEventType.InitialBalanceMigrated, "InitialBalance", 0,
                    entityName: "ترحيل تلقائي عند الترقية",
                    details: $"تم إنشاء {createdCount:N0} رصيد أولي لـ {affectedProductIds.Count:N0} منتج");
        }
    }
}
