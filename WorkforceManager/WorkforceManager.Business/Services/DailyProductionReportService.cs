using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقرير الإنتاج اليومي بالدفعات: المنتج ده خلص منه كام النهارده، وكام
    /// لسه واقف وعند أنهي مرحلة.
    ///
    /// الفرق عن تقرير الإنتاج القديم: القديم بيعد القطع اللي عدّت آخر مرحلة
    /// في اليوم ده وخلاص. ده بيقول كمان **الشغل اللي تحت الإيد** — الرقم
    /// اللي بيخلي مدير الإنتاج يعرف الخط واقف فين وبكرة هيبدأ منين.
    ///
    /// قاعدة الاحتساب المتفق عليها: القطعة بتتحسب إنتاج مكتمل **يوم ما
    /// خلصت الخط فعلاً**، مش يوم ما بدأت. تقرير يوم قديم عمره ما بيتغير
    /// بأثر رجعي — بس بيقول "منها كذا مرحّلة من قبل كده" عشان الصورة تبان.
    /// </summary>
    public class DailyProductionReportService
    {
        private readonly IProductionBatchRepository _batchRepo;
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IProductRepository _productRepo;

        public DailyProductionReportService(
            IProductionBatchRepository batchRepo,
            IProductionDayClosureRepository closureRepo,
            IProductRepository productRepo)
        {
            _batchRepo = batchRepo;
            _closureRepo = closureRepo;
            _productRepo = productRepo;
        }

        public async Task<DailyProductionReportDto> GetAsync(DateTime date)
        {
            var day = date.Date;

            var completed = await _batchRepo.GetCompletedOnAsync(day);
            var parked = await _batchRepo.GetOpenAsOfAsync(day);
            var closure = await _closureRepo.GetByDateAsync(day);

            var products = await _productRepo.GetAllWithStagesAsync();
            var lineByProduct = products.ToDictionary(
                p => p.Id, p => ProductionBatchService.ActiveLine(p));
            var nameById = products.ToDictionary(p => p.Id, p => p.Name);

            // كل منتج فيه حركة النهارده (خلص منه حاجة أو واقف له حاجة)
            var productIds = completed.Select(b => b.ProductId)
                .Concat(parked.Select(b => b.ProductId))
                .Distinct()
                .ToList();

            var rows = new List<DailyProductReportDto>();
            foreach (var productId in productIds)
            {
                if (!nameById.TryGetValue(productId, out var productName)) continue;
                lineByProduct.TryGetValue(productId, out var line);
                line ??= new List<ProductionStage>();

                var productCompleted = completed.Where(b => b.ProductId == productId).ToList();
                var productParked = parked.Where(b => b.ProductId == productId).ToList();

                rows.Add(new DailyProductReportDto
                {
                    ProductId = productId,
                    ProductName = productName,
                    CompletedPieces = productCompleted.Sum(b => b.Quantity),
                    // "مرحّلة" = بدأت في يوم أقدم وخلصت النهارده
                    CompletedFromCarriedPieces = productCompleted
                        .Where(b => b.WasCarriedOver)
                        .Sum(b => b.Quantity),
                    ParkedLots = productParked
                        .Select(b => ToLot(b, line, day))
                        .OrderBy(l => l.NextStageOrder)
                        .ToList()
                });
            }

            return new DailyProductionReportDto
            {
                Date = day,
                IsClosed = closure is not null,
                ClosedAt = closure?.ClosedAt,
                Products = rows
                    .Where(r => r.HasActivity)
                    .OrderByDescending(r => r.CompletedPieces)
                    .ThenBy(r => r.ProductName)
                    .ToList()
            };
        }

        private static ParkedLotDto ToLot(ProductionBatch batch, List<ProductionStage> line, DateTime day)
        {
            var next = ProductionBatchService.NextStage(batch, line);
            var nextIndex = next is null ? -1 : line.FindIndex(s => s.Id == next.Id);

            return new ParkedLotDto
            {
                BatchId = batch.Id,
                Quantity = batch.Quantity,
                // خط الإنتاج اتغيّر بعد ما الدفعة بدأت — بنقولها صريح بدل
                // ما نخبّي دفعة ضايعة عن مدير الإنتاج
                NextStageName = next?.StageName ?? "خط الإنتاج اتغيّر — راجع المراحل",
                NextStageOrder = nextIndex + 1,
                StartedDate = batch.StartedDate,
                DaysWaiting = Math.Max(0, (int)(day - batch.StartedDate.Date).TotalDays)
            };
        }
    }
}
