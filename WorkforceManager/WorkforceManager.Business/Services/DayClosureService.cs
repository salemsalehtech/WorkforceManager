using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// إقفال إنتاج اليوم: المستخدم **بيشوف** أرقام اليوم ويوافق عليها،
    /// فاليوم بياخد حالة نهائية ومبقاش ينفع يتسجل عليه إنتاج جديد يغيّر
    /// أرقام تقرير اتطبع خلاص.
    ///
    /// الواقف مش بيتحرّك ولا بيترحّل — هو أصلاً محسوب من سجلات الإنتاج،
    /// فالقطع اللي معدّتش المرحلة الجاية بتفضل بانها واقفة لوحدها بكرة
    /// وبعد بكرة لحد ما حد يشتغل عليها.
    ///
    /// الإقفال قابل للفتح تاني — الغلط في الإدخال وارد، وحبس المستخدم بره
    /// يومه مش حل.
    /// </summary>
    public class DayClosureService
    {
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly DailyProductionReportService _reportService;
        private readonly IUnitOfWork _unitOfWork;

        public DayClosureService(
            IProductionDayClosureRepository closureRepo,
            DailyProductionReportService reportService,
            IUnitOfWork unitOfWork)
        {
            _closureRepo = closureRepo;
            _reportService = reportService;
            _unitOfWork = unitOfWork;
        }

        /// <summary>اليوم ده مقفول؟</summary>
        public Task<bool> IsClosedAsync(DateTime date) => _closureRepo.IsClosedAsync(date);

        /// <summary>
        /// معاينة الإقفال: خلص كام النهارده وكام لسه واقف وعند أنهي مرحلة.
        /// دي اللي بتتعرض للمستخدم قبل ما يوافق — مفيش يوم بيتقفل من غير
        /// ما يشوف أرقامه.
        /// </summary>
        public async Task<DayClosurePreviewDto> PreviewAsync(DateTime date)
        {
            var report = await _reportService.GetAsync(date);
            var parked = await _reportService.GetAllParkedAsync(date);

            return new DayClosurePreviewDto
            {
                Date = date.Date,
                AlreadyClosed = await _closureRepo.IsClosedAsync(date),
                CompletedPieces = report.TotalCompletedPieces,
                ParkedPieces = parked.Sum(p => p.ParkedPieces),
                HasOverCounting = parked.Any(p => p.HasOverCounting),
                ParkedByProduct = parked.Select(ToParkedProduct).ToList()
            };
        }

        /// <summary>
        /// يقفل اليوم بعد موافقة المستخدم. بيخزّن لقطة من أرقام اليوم عشان
        /// التقرير يعرف يقول "اليوم ده اتقفل بالأرقام دي" حتى لو حد صحّح
        /// سجل قديم بعد كده.
        /// </summary>
        public async Task<ProductionDayClosure> CloseAsync(DateTime date, string? notes = null)
        {
            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            // التحقق جوه القفل: نسختين بيقفلوا نفس اليوم في نفس اللحظة
            // هيكسروا الفهرس الفريد، والرسالة دي أوضح من خطأ قاعدة بيانات
            if (await _closureRepo.IsClosedAsync(date))
                throw new InvalidOperationException($"إنتاج يوم {date:yyyy/MM/dd} مقفول بالفعل");

            var preview = await PreviewAsync(date);

            var closure = new ProductionDayClosure
            {
                Date = date.Date,
                ClosedAt = DateTime.Now,
                CompletedPieces = preview.CompletedPieces,
                ParkedPieces = preview.ParkedPieces,
                Notes = notes
            };

            await _closureRepo.AddAsync(closure);
            await _closureRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return closure;
        }

        /// <summary>أكتر مرحلة متكدّس عندها شغل — دي اللي محتاجة عمال بكرة</summary>
        private static ParkedProductDto ToParkedProduct(DailyProductReportDto product)
        {
            var biggest = product.StageWip
                .OrderByDescending(w => w.WaitingPieces)
                .FirstOrDefault();

            return new ParkedProductDto
            {
                ProductName = product.ProductName,
                ParkedPieces = product.ParkedPieces,
                BiggestQueueStage = biggest?.StageName ?? "—",
                BiggestQueuePieces = biggest?.WaitingPieces ?? 0
            };
        }

        /// <summary>يفتح يوم مقفول عشان يتعدّل (الغلط في الإدخال وارد)</summary>
        public async Task ReopenAsync(DateTime date)
        {
            var closure = await _closureRepo.GetByDateAsync(date)
                ?? throw new InvalidOperationException($"يوم {date:yyyy/MM/dd} مش مقفول أصلاً");

            _closureRepo.Remove(closure);
            await _closureRepo.SaveChangesAsync();
        }
    }
}
