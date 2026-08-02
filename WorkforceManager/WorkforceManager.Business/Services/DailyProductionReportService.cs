using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقرير الإنتاج اليومي: المنتج ده طلع منه كام تام النهارده، وكام قطعة
    /// لسه مستنية عند كل مرحلة.
    ///
    /// **الرقمين دول محسوبين، مش مخزّنين.** مفيش جدول بيتتبّع القطع وهي
    /// ماشية في الخط — كله بيتحسب من سجلات الإنتاج نفسها:
    ///
    ///   • التام النهارده  = إنتاج آخر مرحلة في الخط في اليوم ده
    ///   • الواقف قبل مرحلة = إجمالي اللي خلص المرحلة اللي قبلها من أول
    ///                        التسجيل، ناقص إجمالي اللي خلص المرحلة دي
    ///
    /// ليه كده: القطعة اللي عدّت المرحلة السادسة ومعدّتش السابعة هي بالتعريف
    /// واقفة قبل السابعة. الطرح ده بيقول الحقيقة من غير ما المستخدم يجاوب
    /// ولا سؤال زيادة عن "القطع دي جاية منين".
    ///
    /// المقابل: مفيش تتبّع لقطعة بعينها (بدأت إمتى، مشيت إزاي). ده قرار
    /// مقصود — المصنع محتاج الإجماليات مش رحلة كل لوط.
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
            var toDate = await _productionRepo.GetStageTotalsUpToAsync(day);
            var closure = await _closureRepo.GetByDateAsync(day);
            var products = await _productRepo.GetAllWithStagesAsync();

            var rows = products
                .Select(product => Describe(product, today, toDate))
                .Where(row => row.HasActivity)
                .OrderByDescending(row => row.CompletedPieces)
                .ThenByDescending(row => row.ParkedPieces)
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

        /// <summary>الواقف في المصنع كله بنهاية يوم معين — لشاشة الإقفال</summary>
        public async Task<IReadOnlyList<DailyProductReportDto>> GetAllParkedAsync(DateTime date)
        {
            var day = date.Date;
            var today = await _productionRepo.GetStageTotalsOnAsync(day);
            var toDate = await _productionRepo.GetStageTotalsUpToAsync(day);
            var products = await _productRepo.GetAllWithStagesAsync();

            return products
                .Select(product => Describe(product, today, toDate))
                .Where(row => row.ParkedPieces > 0 || row.HasOverCounting)
                .OrderByDescending(row => row.ParkedPieces)
                .ToList();
        }

        /// <summary>مراحل المنتج النشطة بترتيب الخط — مصدر الحقيقة لكل الحسابات هنا</summary>
        public static List<ProductionStage> ActiveLine(Product product) =>
            product.Stages
                .Where(s => s.IsActive)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .ToList();

        private static DailyProductReportDto Describe(
            Product product,
            IReadOnlyDictionary<int, int> today,
            IReadOnlyDictionary<int, int> toDate)
        {
            var line = ActiveLine(product);
            if (line.Count == 0)
                return new DailyProductReportDto { ProductId = product.Id, ProductName = product.Name };

            int Today(ProductionStage s) => today.TryGetValue(s.Id, out var v) ? v : 0;
            int ToDate(ProductionStage s) => toDate.TryGetValue(s.Id, out var v) ? v : 0;

            // الواقف قبل كل مرحلة = اللي خلص اللي قبلها ناقص اللي خلصها هي.
            // أول مرحلة مالهاش واقف — مفيش مرحلة قبلها القطع تستنى بعدها
            var wip = new List<StageWipDto>();
            for (var i = 1; i < line.Count; i++)
            {
                var waiting = ToDate(line[i - 1]) - ToDate(line[i]);
                if (waiting == 0) continue;

                wip.Add(new StageWipDto
                {
                    StageId = line[i].Id,
                    StageName = line[i].StageName,
                    StageOrder = i + 1,
                    // الرقم السالب معناه المرحلة اتسجل عليها أكتر من اللي
                    // قبلها — مستحيل يحصل فعليًا، فبنصفّر الواقف وبنرفع علم
                    WaitingPieces = Math.Max(0, waiting),
                    IsOverCounted = waiting < 0,
                    OverCountedBy = waiting < 0 ? -waiting : 0
                });
            }

            return new DailyProductReportDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CompletedPieces = Today(line[^1]),
                StartedPieces = Today(line[0]),
                StageWip = wip
            };
        }
    }
}
