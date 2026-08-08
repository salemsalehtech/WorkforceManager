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
    ///   • التام النهارده = إنتاج آخر مرحلة نشطة في اليوم ده
    ///   • الداخل الخط    = إنتاج أول مرحلة في اليوم ده
    ///
    /// كان فيه كمان حساب لـ"الواقف بين المراحل" (الفرق التراكمي بين كل
    /// مرحلتين). اتشال بناءً على طلب المستخدم: الرقم كان بيتعرض في شاشة
    /// التسجيل والتقارير والإقفال من غير ما حد ياخد قرار بناءً عليه.
    /// **متترجعش تاني** — رجوعه معناه رجوع شارات الطوابير على كل مرحلة
    /// وتحذيرات العد الزايد ورقم تالت في كل ملخص.
    /// </summary>
    public class DailyProductionReportService
    {
        private readonly IDailyProductionRepository _productionRepo;
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IProductRepository _productRepo;

        public DailyProductionReportService(
            IDailyProductionRepository productionRepo,
            IProductionDayClosureRepository closureRepo,
            IProductRepository productRepo)
        {
            _productionRepo = productionRepo;
            _closureRepo = closureRepo;
            _productRepo = productRepo;
        }

        public async Task<DailyProductionReportDto> GetAsync(DateTime date)
        {
            var day = date.Date;

            var today = await _productionRepo.GetStageTotalsOnAsync(day);
            var closure = await _closureRepo.GetByDateAsync(day);
            var products = await _productRepo.GetAllWithStagesAsync();

            var rows = products
                .Select(product => Describe(product, today))
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

        private static DailyProductReportDto Describe(
            Product product, IReadOnlyDictionary<int, int> today)
        {
            var line = ProductionLine.Active(product);
            if (line.Count == 0)
                return new DailyProductReportDto { ProductId = product.Id, ProductName = product.Name };

            int Today(ProductionStage s) => today.TryGetValue(s.Id, out var v) ? v : 0;

            return new DailyProductReportDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CompletedPieces = Today(line[^1]),
                StartedPieces = Today(line[0])
            };
        }
    }
}
