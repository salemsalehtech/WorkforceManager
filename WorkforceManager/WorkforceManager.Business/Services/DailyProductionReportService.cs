using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقرير الإنتاج اليومي: كل منتج طلع منه كام تام النهارده وكام دخل الخط.
    ///
    /// **الرقمين محسوبين، مش مخزّنين.** مفيش جدول بيتتبّع القطع وهي ماشية
    /// في الخط — كله بيتحسب من سجلات الإنتاج نفسها:
    ///
    ///   • التام النهارده = الإنتاج الفعلي لآخر مرحلة نشطة في اليوم ده
    ///   • الداخل الخط    = الإنتاج الفعلي لأول مرحلة في اليوم ده
    ///
    /// "الإنتاج الفعلي" (<see cref="ProductionStageOutputService"/>) مش
    /// مجموع قطع العمال — رقم منفصل تمامًا، شوف توثيقه.
    ///
    /// كان فيه كمان حساب لـ"الواقف بين المراحل" (الفرق التراكمي بين كل
    /// مرحلتين). اتشال بناءً على طلب المستخدم: الرقم كان بيتعرض في شاشة
    /// التسجيل والتقارير والإقفال من غير ما حد ياخد قرار بناءً عليه.
    /// **متترجعش تاني** — رجوعه معناه رجوع شارات الطوابير على كل مرحلة
    /// وتحذيرات العد الزايد ورقم تالت في كل ملخص.
    /// </summary>
    public class DailyProductionReportService
    {
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IProductRepository _productRepo;
        private readonly ScrapService _scrap;
        private readonly ProductionStageOutputService _productionOutput;

        public DailyProductionReportService(
            IProductionDayClosureRepository closureRepo,
            IProductRepository productRepo,
            ScrapService scrap,
            ProductionStageOutputService productionOutput)
        {
            _closureRepo = closureRepo;
            _productRepo = productRepo;
            _scrap = scrap;
            _productionOutput = productionOutput;
        }

        public async Task<DailyProductionReportDto> GetAsync(DateTime date)
        {
            var day = date.Date;

            var today = await _productionOutput.GetStageTotalsOnAsync(day);
            var scrapToday = await _scrap.GetStageTotalsOnAsync(day);
            var closure = await _closureRepo.GetByDateAsync(day);
            var products = await _productRepo.GetAllWithStagesAsync();

            var rows = products
                .Select(product => Describe(product, today, scrapToday))
                .Where(row => row.HasActivity)
                .OrderByDescending(row => row.CompletedPieces)
                .ThenBy(row => row.ProductName)
                .ToList();

            return new DailyProductionReportDto
            {
                Date = day,
                IsClosed = closure is not null,
                ClosedAt = closure?.ClosedAt,
                Products = rows
            };
        }

        /// <summary>
        /// نفس تقرير GetAsync بالظبط، بس مجموع على مدى أيام (أسبوع/شهر)
        /// بدل يوم واحد — كل يوم في المدى بياخد نفس صيغة "تام/داخل/هالك"
        /// المستخدمة في GetAsync، والنتيجة مجموع الأيام. الإقفال مفهوم
        /// يوم واحد بس، فبيرجع IsClosed=false دايمًا هنا.
        /// </summary>
        public async Task<DailyProductionReportDto> GetForRangeAsync(DateTime from, DateTime to)
        {
            var fromDay = from.Date;
            var toDay = to.Date;
            var products = await _productRepo.GetAllWithStagesAsync();

            var completed = new Dictionary<int, int>();
            var started = new Dictionary<int, int>();
            var scrapTotals = new Dictionary<int, int>();

            for (var day = fromDay; day <= toDay; day = day.AddDays(1))
            {
                var dayOutput = await _productionOutput.GetStageTotalsOnAsync(day);
                var dayScrap = await _scrap.GetStageTotalsOnAsync(day);

                foreach (var product in products)
                {
                    var dayRow = Describe(product, dayOutput, dayScrap);
                    if (!dayRow.HasActivity) continue;

                    completed[product.Id] = completed.GetValueOrDefault(product.Id) + dayRow.CompletedPieces;
                    started[product.Id] = started.GetValueOrDefault(product.Id) + dayRow.StartedPieces;
                    scrapTotals[product.Id] = scrapTotals.GetValueOrDefault(product.Id) + dayRow.ScrapPieces;
                }
            }

            var activeProductIds = completed.Keys
                .Union(started.Keys)
                .Union(scrapTotals.Keys)
                .ToHashSet();

            var rows = products
                .Where(p => activeProductIds.Contains(p.Id))
                .Select(p => new DailyProductReportDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    CompletedPieces = completed.GetValueOrDefault(p.Id),
                    StartedPieces = started.GetValueOrDefault(p.Id),
                    ScrapPieces = scrapTotals.GetValueOrDefault(p.Id)
                })
                .OrderByDescending(row => row.CompletedPieces)
                .ThenBy(row => row.ProductName)
                .ToList();

            return new DailyProductionReportDto
            {
                Date = fromDay,
                IsClosed = false,
                ClosedAt = null,
                Products = rows
            };
        }

        private static DailyProductReportDto Describe(
            Product product,
            IReadOnlyDictionary<int, int> today,
            IReadOnlyDictionary<int, int> scrapToday)
        {
            var line = ProductionLine.Active(product);
            if (line.Count == 0)
                return new DailyProductReportDto { ProductId = product.Id, ProductName = product.Name };

            int Today(ProductionStage s) => today.TryGetValue(s.Id, out var v) ? v : 0;
            int Scrap(ProductionStage s) => scrapToday.TryGetValue(s.Id, out var v) ? v : 0;

            return new DailyProductReportDto
            {
                ProductId = product.Id,
                ProductName = product.Name,

                // التام = آخر مرحلة ناقص هالكها. القطعة اللي خلصت الخط
                // والجودة رفضتها مش منتج تام — ودي الحالة الوحيدة اللي
                // الفرق بين المراحل مش بيكشفها لأن مفيش مرحلة بعدها
                CompletedPieces = Math.Max(0, Today(line[^1]) - Scrap(line[^1])),
                StartedPieces = Today(line[0]),

                // الهالك على كل مراحل المنتج — مش على آخر مرحلة بس
                ScrapPieces = line.Sum(Scrap)
            };
        }
    }
}
