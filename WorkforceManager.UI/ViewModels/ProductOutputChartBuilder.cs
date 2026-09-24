using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>نتيجة بناء رسم إنتاج المنتجات — كل اللي الشاشة محتاجة تعرضه، جاهز</summary>
    public sealed class ProductOutputChartResult
    {
        public IReadOnlyList<ChartBucket> Buckets { get; init; } = [];
        public IReadOnlyList<ChartLegendItem> Legend { get; init; } = [];
        public int TotalCompleted { get; init; }
        public int TotalScrap { get; init; }
        public string ScrapRateText { get; init; } = "";
        public string AverageText { get; init; } = "";
        public bool HasData { get; init; }
    }

    /// <summary>
    /// بيبني أعمدة رسم إنتاج المنتجات ومفتاح ألوانه من نقاط
    /// ProductionChartService — **المكان الوحيد** اللي بيحوّل النقط لأعمدة،
    /// مشترك بين شاشة "التقييم والمتابعة" (المدى الكامل بالفلتر) وشاشة
    /// الرئيسية (أسبوع الشغل الحالي بس). كان جوّه ReportsViewModel، واتنقل
    /// هنا عشان الرئيسية تعرض نفس الرسم بالظبط مش نسخة تانية بترسم بطريقتها.
    ///
    /// **ألوانه مفاتيح فُرَش مش أكواد ألوان** (شوف <see cref="ThemeBrush"/>) —
    /// التبديل الحي للثيم بيوصل للرسم زي أي عنصر تاني.
    /// </summary>
    public static class ProductOutputChartBuilder
    {
        /// <summary>
        /// سلاسل المنتجات — ترتيب ثابت، بيتوزّع على المنتجات بالمعرّف
        /// مش بالترتيب، فالمنتج بياخد نفس اللون مهما اتغيّر الفلتر.
        /// </summary>
        public static readonly string[] Palette =
        {
            "Series1Brush", "Series2Brush", "Series3Brush", "Series4Brush",
            "Series5Brush", "Series6Brush", "Series7Brush", "Series8Brush"
        };

        /// <summary>
        /// لون المنتجات اللي خرجت برّه اللوحة (التاسع فما فوق).
        ///
        /// توليد ألوان جديدة أو لفّ اللوحة من أولها كان بيدي لونين
        /// متطابقين لمنتجين مختلفين — والمستخدم مش هيعرف إن ده حصل.
        /// </summary>
        private const string OtherProductsColor = "SeriesOtherBrush";

        private const string OtherProductsLabel = "منتجات تانية";

        /// <summary>لون الهالك — مميز عن ألوان المنتجات عن قصد</summary>
        private const string ScrapColor = "DangerBrush";

        /// <summary>الفاصل بين شرايح العمود الواحد — بيخلي الحدود تبان</summary>
        private const double SegmentGap = 2;

        /// <param name="points">نقاط الفترة (بعد أي فلتر منتجات)</param>
        /// <param name="firstBucket">بداية أول عمود</param>
        /// <param name="lastDay">آخر يوم يترسمله عمود — النهارده في التقييم، آخر يوم في الأسبوع في الرئيسية (الأيام الجاية بتظهر فاضية)</param>
        /// <param name="previousByProduct">قطع كل منتج في الفترة اللي قبلها بنفس الطول — لتغيّر مفتاح الألوان</param>
        /// <param name="maxBarHeight">أقصى ارتفاع للعمود بالبكسل — لازم يفضل أقل من ارتفاع منطقة الرسم في ProductOutputChart بفرق بسيط</param>
        public static ProductOutputChartResult Build(
            IReadOnlyList<ProductOutputPointDto> points,
            DateTime firstBucket,
            DateTime lastDay,
            ChartGrain grain,
            IReadOnlyDictionary<int, int> previousByProduct,
            double maxBarHeight)
        {
            // المنتجات مرتبة بالأكتر إنتاجًا، ولون ثابت لكل منتج
            var productTotals = points
                .GroupBy(p => (p.ProductId, p.ProductName))
                .Select(g => (g.Key.ProductId, g.Key.ProductName, Total: g.Sum(x => x.CompletedPieces)))
                .OrderByDescending(x => x.Total)
                .ToList();

            var namedProducts = productTotals.Take(Palette.Length).ToList();

            var colorByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Color: Palette[i]))
                .ToDictionary(x => x.ProductId, x => x.Color);

            string ColorFor(int productId) =>
                colorByProduct.TryGetValue(productId, out var color) ? color : OtherProductsColor;

            // ترتيب الشرايح جوه العمود: نفس ترتيب المفتاح دايمًا، عشان
            // العين تلاقي المنتج في نفس المكان من فترة للتانية
            var orderByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Order: i))
                .ToDictionary(x => x.ProductId, x => x.Order);

            int OrderFor(int productId) =>
                orderByProduct.TryGetValue(productId, out var order) ? order : Palette.Length;

            var legend = BuildLegend(productTotals, namedProducts, ColorFor, previousByProduct);

            var pointsByBucket = points.ToLookup(p => p.BucketStart);
            var currentBucket = ProductionChartService.BucketOf(DateTime.Today, grain).Start;

            // المقياس على **إجمالي الفترة + هالكها**: العمود بقى بيحمل
            // الاتنين، فلو المقياس على التام لوحده الهالك بيطلع برّه
            var bucketTotals = points
                .GroupBy(p => p.BucketStart)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.CompletedPieces) + g.Sum(p => p.ScrapPieces));

            var maxBucketTotal = bucketTotals.Count == 0 ? 1 : Math.Max(1, bucketTotals.Values.Max());

            // المتوسط على الفترات اللي فيها شغل بس: الفترات الفاضية
            // بتنزّل المتوسط لرقم مالوش معنى (أجازات ويوم الجمعة)
            var workedTotals = points
                .GroupBy(p => p.BucketStart)
                .Select(g => g.Sum(p => p.CompletedPieces))
                .Where(t => t > 0)
                .ToList();

            var average = workedTotals.Count == 0 ? 0 : workedTotals.Average();
            var averageOffset = average / maxBucketTotal * maxBarHeight;
            var showAverage = workedTotals.Count >= 2;

            var buckets = new List<ChartBucket>();

            // كل فترات المدى بالترتيب الزمني (حتى الفاضية — محور الزمن
            // لازم يكون متصل)
            for (var bucket = firstBucket;
                 bucket <= lastDay;
                 bucket = ProductionChartService.NextBucket(bucket, grain))
            {
                var bucketPoints = pointsByBucket[bucket]
                    .OrderBy(p => OrderFor(p.ProductId))
                    .ThenBy(p => p.ProductName)
                    .ToList();

                var completed = bucketPoints.Sum(p => p.CompletedPieces);
                var scrapped = bucketPoints.Sum(p => p.ScrapPieces);
                var end = ProductionChartService.BucketOf(bucket, grain).End;

                double HeightOf(int pieces) =>
                    pieces <= 0 ? 0 : Math.Max(3, (double)pieces / maxBucketTotal * maxBarHeight - SegmentGap);

                var segments = bucketPoints
                    .Where(p => p.CompletedPieces > 0)
                    .Select(p => new ChartBar
                    {
                        Color = ColorFor(p.ProductId),
                        Height = HeightOf(p.CompletedPieces),
                        Tooltip = $"{p.ProductName}\n{LabelFor(bucket, end, grain)}\n" +
                                  $"{p.CompletedPieces:N0} قطعة مكتملة"
                    })
                    .ToList();

                // الهالك فوق العمود: بيبان كزيادة على الشغل، مش جزء منه
                if (scrapped > 0)
                    segments.Insert(0, new ChartBar
                    {
                        Color = ScrapColor,
                        Height = HeightOf(scrapped),
                        Tooltip = $"هالك\n{LabelFor(bucket, end, grain)}\n{scrapped:N0} قطعة"
                    });

                buckets.Add(new ChartBucket
                {
                    Label = ShortLabel(bucket, grain),
                    Total = completed,
                    TotalText = completed == 0 ? "" : $"{completed:N0}",
                    HasWork = completed > 0 || scrapped > 0,
                    IsCurrent = bucket == currentBucket,
                    AverageOffset = averageOffset,
                    ShowAverage = showAverage,
                    Segments = segments
                });
            }

            var totalCompleted = points.Sum(p => p.CompletedPieces);
            var totalScrap = points.Sum(p => p.ScrapPieces);

            // زي شاشة اليوم: "0%" جنب "0 قطعة" بتقول نفس الحاجة مرتين
            var baseline = totalCompleted + totalScrap;

            return new ProductOutputChartResult
            {
                Buckets = buckets,
                Legend = legend,
                TotalCompleted = totalCompleted,
                TotalScrap = totalScrap,
                ScrapRateText = totalScrap == 0 || baseline == 0
                    ? ""
                    : $"{(double)totalScrap / baseline * 100:0.#}% من الشغل",
                AverageText = showAverage ? $"متوسط {UnitName(grain)}: {average:N0} قطعة" : "",
                HasData = points.Count > 0
            };
        }

        private static List<ChartLegendItem> BuildLegend(
            List<(int ProductId, string ProductName, int Total)> productTotals,
            List<(int ProductId, string ProductName, int Total)> namedProducts,
            Func<int, string> colorFor,
            IReadOnlyDictionary<int, int> previousByProduct)
        {
            var legend = new List<ChartLegendItem>();

            foreach (var p in namedProducts)
            {
                var (changeText, changeColor) = DescribeChange(
                    p.Total, previousByProduct.TryGetValue(p.ProductId, out var before) ? before : 0);

                legend.Add(new ChartLegendItem
                {
                    Color = colorFor(p.ProductId),
                    ProductName = p.ProductName,
                    TotalText = $"{p.Total:N0} قطعة",
                    ChangeText = changeText,
                    ChangeColor = changeColor
                });
            }

            var otherTotal = productTotals.Skip(Palette.Length).Sum(p => p.Total);
            if (otherTotal > 0)
                legend.Add(new ChartLegendItem
                {
                    Color = OtherProductsColor,
                    ProductName = $"{OtherProductsLabel} ({productTotals.Count - namedProducts.Count})",
                    TotalText = $"{otherTotal:N0} قطعة"
                });

            return legend;
        }

        /// <summary>
        /// نسبة التغيّر عن نفس الرقم في الفترة اللي قبلها، كنص ("▲ 12%") +
        /// مفتاح فرشة. بترجع فاضي لو مفيش أساس للمقارنة — "زاد ∞%" مش معلومة.
        /// **مصدر أسهم المقارنة الوحيد في البرنامج** — مفتاح ألوان الرسم
        /// وكروت الرئيسية الاتنين بيستخدموه.
        ///
        /// <paramref name="higherIsBetter"/> false لرقم الزيادة فيه وحشة (زي
        /// الغياب): السهم بيفضل بيقول الاتجاه الحقيقي، واللون بس اللي بيتقلب.
        /// </summary>
        public static (string Text, string Color) DescribeChange(int now, int before, bool higherIsBetter = true)
        {
            if (before <= 0) return ("", "InkSoftBrush");

            var change = (double)(now - before) / before * 100;
            var (good, bad) = higherIsBetter ? ("GoodBrush", "DangerBrush") : ("DangerBrush", "GoodBrush");

            return change switch
            {
                > 1 => ($"▲ {change:0}%", good),
                < -1 => ($"▼ {Math.Abs(change):0}%", bad),
                _ => ("= زي الفترة اللي فاتت", "InkSoftBrush")
            };
        }

        public static string UnitName(ChartGrain grain) => grain switch
        {
            ChartGrain.Day => "اليوم",
            ChartGrain.Week => "الأسبوع",
            _ => "الشهر"
        };

        /// <summary>عنوان قصير تحت العمود — لازم يفضل مقروء وهو 60 عمود</summary>
        private static string ShortLabel(DateTime bucket, ChartGrain grain) => grain switch
        {
            ChartGrain.Month => $"{bucket:MM/yyyy}",
            _ => $"{bucket:dd/MM}"
        };

        /// <summary>الوصف الكامل في التلميح</summary>
        private static string LabelFor(DateTime start, DateTime end, ChartGrain grain) => grain switch
        {
            ChartGrain.Day => $"يوم {start:yyyy/MM/dd}",
            ChartGrain.Week => $"أسبوع {start:dd/MM} → {end:dd/MM}",
            _ => $"شهر {start:MM/yyyy}"
        };
    }
}
