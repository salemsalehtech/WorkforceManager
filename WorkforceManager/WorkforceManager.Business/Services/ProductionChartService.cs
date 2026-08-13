using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>التقسيم الزمني للرسم البياني</summary>
    public enum ChartGrain
    {
        Day = 1,
        Week = 2,
        Month = 3
    }

    /// <summary>
    /// بيانات الرسم البياني للمنتجات: كام قطعة مكتملة اتنتجت من كل منتج
    /// في كل فترة (يوم / أسبوع / شهر)، وكام هالك اتسجّل عليه.
    ///
    /// "المكتملة" = الإنتاج الفعلي المسجّل على آخر مرحلة في خط إنتاج
    /// المنتج (أعلى ترتيب بين مراحله النشطة) — دي القطع اللي خرجت من
    /// الخط فعلاً، من <see cref="ProductionStageOutputService"/> مش من
    /// مجموع قطع العمال. القطع المسجلة على المراحل الأبكر شغل جارٍ مش
    /// إنتاج مكتمل، وجمعها كان هيعد نفس القطعة أكتر من مرة.
    ///
    /// **الهالك بيتحسب مرتين بمعنيين مختلفين، وده مقصود:**
    /// - اللي **بيتخصم** من التام هو هالك آخر مرحلة بس — قطعة اترمت
    ///   بعد ما خلصت الخط. دي نفس قاعدة ملخص اليوم وقفل اليوم، فالرسم
    ///   بيقول نفس رقم باقي الشاشات.
    /// - اللي **بيتعرض** كهالك هو الهالك كله على كل المراحل — لأن
    ///   السؤال "طلعلي كام هالك؟" معناه كل قطعة خرجت من الخط، سواء
    ///   اترمت في أول مرحلة أو في آخرها.
    /// خصم الهالك المبكر من التام كان هيعده مرتين: هو أصلاً مش داخل
    /// في رقم آخر مرحلة لأنه مكملش لحد هناك.
    /// </summary>
    public class ProductionChartService
    {
        private readonly IProductRepository _productRepo;
        private readonly ScrapService _scrap;
        private readonly ProductionStageOutputService _productionOutput;

        public ProductionChartService(
            IProductRepository productRepo,
            ScrapService scrap,
            ProductionStageOutputService productionOutput)
        {
            _productRepo = productRepo;
            _scrap = scrap;
            _productionOutput = productionOutput;
        }

        /// <summary>
        /// حدود الفترة اللي التاريخ ده واقع فيها. الأسبوع بحدود أسبوع
        /// العمل (خميس → أربع) — نفس تعريف <see cref="WeeklySummaryService"/>،
        /// مش تعريف تاني.
        /// </summary>
        public static (DateTime Start, DateTime End) BucketOf(DateTime date, ChartGrain grain) =>
            grain switch
            {
                ChartGrain.Day => (date.Date, date.Date),
                ChartGrain.Week => WeeklySummaryService.GetWorkWeekRange(date),
                _ => (new DateTime(date.Year, date.Month, 1),
                      new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)))
            };

        /// <summary>بداية الفترة اللي بعد دي — عشان محور الزمن يفضل متصل</summary>
        public static DateTime NextBucket(DateTime bucketStart, ChartGrain grain) =>
            grain switch
            {
                ChartGrain.Day => bucketStart.AddDays(1),
                ChartGrain.Week => bucketStart.AddDays(7),
                _ => bucketStart.AddMonths(1)
            };

        /// <summary>
        /// أول يوم في فترة بتبعد <paramref name="count"/> فترة لورا من
        /// النهارده — بيحدد بداية المدى المعروض.
        /// </summary>
        public static DateTime StartOfLast(int count, ChartGrain grain)
        {
            var start = BucketOf(DateTime.Today, grain).Start;

            for (var i = 1; i < count; i++)
                start = grain switch
                {
                    ChartGrain.Day => start.AddDays(-1),
                    ChartGrain.Week => start.AddDays(-7),
                    _ => start.AddMonths(-1)
                };

            return start;
        }

        /// <summary>
        /// نقاط الرسم: (فترة، منتج، قطع مكتملة، هالك) لكل منتج اتشتغل
        /// عليه خلال المدى. الفترات اللي مفيهاش حاجة مش بترجع نقطة
        /// (الشاشة بتكمّل الأصفار للعرض).
        /// </summary>
        public async Task<List<ProductOutputPointDto>> GetProductOutputAsync(
            DateTime from, DateTime to, ChartGrain grain)
        {
            // حدود المدى مظبوطة على فترات كاملة
            var rangeStart = BucketOf(from, grain).Start;
            var rangeEnd = BucketOf(to, grain).End;

            // آخر مرحلة لكل منتج — نفس القاعدة اللي التقرير العام بيستخدمها
            // بالحرف، لأنها نفس الدالة
            var products = await _productRepo.GetAllWithStagesAsync();
            var lastStageByProduct = ProductionLine.LastStageIdByProduct(products);

            var records = await _productionOutput.GetByRangeAsync(rangeStart, rangeEnd);
            var scrapRecords = await _scrap.GetByRangeAsync(rangeStart, rangeEnd);

            // الهالك اللي بيتخصم من التام: آخر مرحلة بس
            var lastStageScrap = scrapRecords
                .Where(s => lastStageByProduct.TryGetValue(s.ProductId, out var last)
                            && s.ProductionStageId == last)
                .GroupBy(s => (Bucket: BucketOf(s.Date, grain).Start, s.ProductId))
                .ToDictionary(g => g.Key, g => g.Sum(s => s.PieceCount));

            // الهالك المعروض: كل المراحل
            var allScrap = scrapRecords
                .GroupBy(s => (Bucket: BucketOf(s.Date, grain).Start, s.ProductId))
                .ToDictionary(g => g.Key, g => g.Sum(s => s.PieceCount));

            // القطع المكتملة بس: السجلات اللي على آخر مرحلة لمنتجها
            var completed = records
                .Where(r => lastStageByProduct.TryGetValue(r.ProductId, out var lastStageId)
                            && r.ProductionStageId == lastStageId)
                .GroupBy(r => (Bucket: BucketOf(r.Date, grain).Start, r.ProductId))
                .ToDictionary(
                    g => g.Key,
                    g => (Name: g.First().ProductName, Pieces: g.Sum(r => r.PieceCount)));

            // اسم المنتج للمفاتيح اللي فيها هالك من غير إنتاج مكتمل —
            // منتج كل شغل الفترة عليه اتهلك لازم يظهر برضه، مش يختفي
            var nameById = products.ToDictionary(p => p.Id, p => p.Name);

            var keys = completed.Keys.Union(allScrap.Keys).ToList();

            return keys
                .Select(key => new ProductOutputPointDto
                {
                    BucketStart = key.Bucket,
                    BucketEnd = BucketOf(key.Bucket, grain).End,
                    ProductId = key.ProductId,
                    ProductName = completed.TryGetValue(key, out var done)
                        ? done.Name
                        : nameById.TryGetValue(key.ProductId, out var name) ? name : "—",
                    CompletedPieces = Math.Max(0,
                        (completed.TryGetValue(key, out var c) ? c.Pieces : 0)
                        - (lastStageScrap.TryGetValue(key, out var lateScrap) ? lateScrap : 0)),
                    ScrapPieces = allScrap.TryGetValue(key, out var scrap) ? scrap : 0
                })
                .OrderBy(p => p.BucketStart).ThenByDescending(p => p.CompletedPieces)
                .ToList();
        }
    }
}
