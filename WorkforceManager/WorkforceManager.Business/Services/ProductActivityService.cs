using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// نشاط المنتجات في فترة: مين اشتغل عليه فعلًا، وأنتج كام، ومين
    /// العمال اللي شغّالين عليه.
    ///
    /// **المكان الوحيد اللي بيقرر "المنتج ده شغّال ولا لأ"**. قبل كده
    /// الشاشة كانت بتقول "نشط" بمعنى فلاج <c>Product.IsActive</c> — وده
    /// بيقول إن المنتج **مسموح** يشتغل عليه، مش إنه اشتغل. منتج متسيب من
    /// شهور كان بيفضل "نشط" على طول، فالرقم مكانش بيقول حاجة.
    ///
    /// دلوقتي الحكم بييجي من سجلات الإنتاج نفسها: فيه شغل في الفترة =
    /// شغّال.
    /// </summary>
    public class ProductActivityService
    {
        private readonly IDailyProductionRepository _production;
        private readonly IProductRepository _products;

        public ProductActivityService(
            IDailyProductionRepository production,
            IProductRepository products)
        {
            _production = production;
            _products = products;
        }

        /// <summary>
        /// أسبوع الشغل اللي بيقع فيه التاريخ ده — **نفس تعريف باقي
        /// التطبيق** (الخميس → الأربع، من <see cref="WeeklySummaryService"/>).
        /// تعريف تاني للأسبوع في شاشة واحدة كان هيدي أرقام مختلفة عن
        /// كشف الأجور لنفس الفترة.
        /// </summary>
        public static (DateTime From, DateTime To) CurrentWeek(DateTime anyDate) =>
            WeeklySummaryService.GetWorkWeekRange(anyDate);

        /// <summary>
        /// نشاط كل المنتجات في فترة. بيرجّع سطر لكل منتج — بما فيهم اللي
        /// مفيش عليه شغل (بصفر)، عشان الشاشة تعرف تفلتر وتقارن.
        /// </summary>
        public async Task<IReadOnlyList<ProductActivityDto>> GetAsync(DateTime from, DateTime to)
        {
            var records = await _production.GetByRangeAsync(from, to);
            var products = await _products.GetAllWithStagesAsync();

            // تجميع مرة واحدة بالمنتج: القطع، المراحل اللي اشتغلت،
            // والعمال اللي اشتغلوا
            var byProduct = records
                .Where(r => r.ProductionStage?.Product is not null)
                .GroupBy(r => r.ProductionStage.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        StageWork = g.Sum(r => r.PieceCount),
                        ByStage = g.GroupBy(r => r.ProductionStageId)
                                   .ToDictionary(s => s.Key, s => s.Sum(r => r.PieceCount)),
                        Workers = g.Select(r => r.WorkerId).ToHashSet(),
                        Days = g.Select(r => r.Date.Date).ToHashSet()
                    });

            return products
                .Select(product =>
                {
                    byProduct.TryGetValue(product.Id, out var stats);

                    // خط الإنتاج بترتيبه — نفس ترتيب DailyProductionReportService
                    var line = ProductionLine.Active(product);

                    int OnStage(ProductionStage? stage) =>
                        stage is not null && stats is not null &&
                        stats.ByStage.TryGetValue(stage.Id, out var pieces) ? pieces : 0;

                    return new ProductActivityDto
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        IsActive = product.IsActive,
                        // التام = آخر مرحلة. الداخل = أول مرحلة.
                        CompletedPieces = OnStage(line.Count > 0 ? line[^1] : null),
                        StartedPieces = OnStage(line.Count > 0 ? line[0] : null),
                        StageWorkPieces = stats?.StageWork ?? 0,
                        WorkerIds = stats?.Workers ?? new HashSet<int>(),
                        DaysWorked = stats?.Days.Count ?? 0,
                        StageIds = line.Select(s => s.Id).ToHashSet()
                    };
                })
                // الترتيب بالتام: "أكتر منتج إنتاجًا" معناه أكتر منتج **خرج**
                .OrderByDescending(p => p.CompletedPieces)
                .ThenByDescending(p => p.StageWorkPieces)
                .ThenBy(p => p.ProductName)
                .ToList();
        }
    }
}
