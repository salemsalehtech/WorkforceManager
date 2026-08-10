using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الشغل الواقف في نص الخط — **المكان الوحيد اللي بيحسبه**.
    ///
    /// الفكرة في سطر: القطعة اللي خلصت المرحلة 5 وماخلصتش المرحلة 6 هي
    /// بالتعريف واقفة قدام المرحلة 6. فالواقف قدام مرحلة =
    /// إجمالي اللي خلص اللي قبلها ناقص إجمالي اللي خلصها هي، من أول
    /// التسجيل لحد اليوم المطلوب.
    ///
    /// **مفيش جدول بيتتبّع القطع.** الرقم ده محسوب من سجلات الإنتاج
    /// اللي المستخدم بيدخّلها أصلاً، فمفيش خطوة "ترحيل" ولا كيان دفعة
    /// المستخدم لازم يفهمه ويصونه.
    ///
    /// **مالوش أي علاقة بالأجور.** العامل بياخد يوميته يوم ما اشتغل،
    /// وتكميل المنتج بعدين بيسجّل يوميات للناس اللي كمّلوا بس. السجل
    /// هو الأجر — الواقف مجرد قراية على نفس السجلات.
    /// </summary>
    public class PendingWorkService
    {
        private readonly IDailyProductionRepository _production;
        private readonly IProductRepository _products;
        private readonly ScrapService _scrap;

        public PendingWorkService(
            IDailyProductionRepository production,
            IProductRepository products,
            ScrapService scrap)
        {
            _production = production;
            _products = products;
            _scrap = scrap;
        }

        /// <summary>الشغل الواقف على منتج واحد لحد اليوم ده (شامله)</summary>
        public async Task<ProductPendingDto?> GetForProductAsync(int productId, DateTime asOf)
        {
            var product = await _products.GetWithStagesAsync(productId);
            if (product is null) return null;

            var totals = await _production.GetStageTotalsUpToAsync(asOf);
            var scrap = await _scrap.GetStageTotalsUpToAsync(asOf);
            return Describe(product, totals, scrap);
        }

        /// <summary>الشغل الواقف على كل المنتجات — المنتجات اللي فيها حاجة بس</summary>
        public async Task<IReadOnlyList<ProductPendingDto>> GetAllAsync(DateTime asOf)
        {
            var products = await _products.GetAllWithStagesAsync();
            var totals = await _production.GetStageTotalsUpToAsync(asOf);
            var scrap = await _scrap.GetStageTotalsUpToAsync(asOf);

            return products
                .Select(p => Describe(p, totals, scrap))
                .Where(p => p.HasPending || p.HasDataError)
                .OrderByDescending(p => p.TotalPending)
                .ThenBy(p => p.ProductName)
                .ToList();
        }

        /// <summary>
        /// الحساب نفسه — دالة نقية على مجاميع المراحل.
        ///
        /// مفصولة عن الاستعلام عشان تتختبر لوحدها، ولأن الاستعلام بيتعمل
        /// مرة واحدة لكل المنتجات مش مرة لكل منتج.
        /// </summary>
        private static ProductPendingDto Describe(
            Core.Models.Product product,
            IReadOnlyDictionary<int, int> totals,
            IReadOnlyDictionary<int, int> scrapTotals)
        {
            var line = ProductionLine.Active(product);

            var result = new ProductPendingDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                LastStageId = line.Count > 0 ? line[^1].Id : 0
            };

            if (line.Count < 2) return result;

            int Total(int stageId) => totals.TryGetValue(stageId, out var pieces) ? pieces : 0;
            int Scrap(int stageId) => scrapTotals.TryGetValue(stageId, out var pieces) ? pieces : 0;

            var stages = new List<PendingStageDto>();

            // المقارنة بين كل مرحلة واللي قبلها مباشرة
            for (var i = 1; i < line.Count; i++)
            {
                // **الهالك بيتشال من الواقف.** القطعة اللي خلصت المرحلة
                // اللي قبل وماكملتش مش دايمًا مستنية دورها — ممكن تكون
                // اتشالت خالص. من غير الطرح ده الـ500 هالك بيفضلوا
                // ظاهرين "واقفين" للأبد والبرنامج مستنيهم يتكمّلوا.
                var before = Total(line[i - 1].Id) - Scrap(line[i - 1].Id);
                var current = Total(line[i].Id);
                var difference = before - current;

                if (difference == 0) continue; // مفيش واقف ومفيش غلط

                stages.Add(new PendingStageDto
                {
                    StageId = line[i].Id,
                    StageName = line[i].StageName,
                    Position = i + 1,
                    PreviousStageName = line[i - 1].StageName,
                    // الفرق الموجب = واقف. السالب = المرحلة اتسجل عليها
                    // أكتر من اللي قبلها، وده مستحيل فيزيائيًا
                    PendingPieces = Math.Max(0, difference),
                    IsDataError = difference < 0,
                    ExcessPieces = Math.Max(0, -difference)
                });
            }

            return new ProductPendingDto
            {
                ProductId = result.ProductId,
                ProductName = result.ProductName,
                LastStageId = result.LastStageId,
                Stages = stages,
                // اللي دخل الخط ناقص اللي خرج منه **وناقص اللي اتشال في
                // الطريق**. بيتصفّر عند السالب: إجمالي سالب معناه إن
                // البيانات فيها غلط مش إن الخط أنتج أكتر من اللي دخله
                TotalPending = Math.Max(0,
                    Total(line[0].Id)
                    - Total(line[^1].Id)
                    - line.Take(line.Count - 1).Sum(s => Scrap(s.Id)))
            };
        }
    }
}
