using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// إقفال إنتاج اليوم. الترحيل نفسه مش عملية — الدفعة المفتوحة بتفضل
    /// مفتوحة لوحدها لحد ما حد يكمّلها. اللي بيحصل هنا إن المستخدم **بيشوف**
    /// الواقف ويوافق عليه، فاليوم بياخد حالة نهائية ومبقاش ينفع يتسجل عليه
    /// إنتاج جديد يغيّر أرقام تقرير اتطبع خلاص.
    ///
    /// الإقفال قابل للفتح تاني — الغلط في الإدخال وارد، وحبس المستخدم بره
    /// يومه مش حل.
    /// </summary>
    public class DayClosureService
    {
        private readonly IProductionDayClosureRepository _closureRepo;
        private readonly IProductionBatchRepository _batchRepo;
        private readonly ProductionBatchService _batchService;
        private readonly IUnitOfWork _unitOfWork;

        public DayClosureService(
            IProductionDayClosureRepository closureRepo,
            IProductionBatchRepository batchRepo,
            ProductionBatchService batchService,
            IUnitOfWork unitOfWork)
        {
            _closureRepo = closureRepo;
            _batchRepo = batchRepo;
            _batchService = batchService;
            _unitOfWork = unitOfWork;
        }

        /// <summary>اليوم ده مقفول؟</summary>
        public Task<bool> IsClosedAsync(DateTime date) => _closureRepo.IsClosedAsync(date);

        /// <summary>
        /// معاينة الإقفال: إيه اللي هيترحّل لبكرة وإيه اللي خلص النهارده.
        /// دي اللي بتتعرض للمستخدم قبل ما يوافق — مفيش حاجة بتترحّل من غير
        /// ما يشوفها.
        /// </summary>
        public async Task<DayClosurePreviewDto> PreviewAsync(DateTime date)
        {
            var parked = await _batchService.GetAllParkedAsync(date);
            var completed = await _batchRepo.GetCompletedOnAsync(date);

            return new DayClosurePreviewDto
            {
                Date = date.Date,
                AlreadyClosed = await _closureRepo.IsClosedAsync(date),
                CarriedLots = parked.ToList(),
                CompletedPieces = completed.Sum(b => b.Quantity)
            };
        }

        /// <summary>
        /// يقفل اليوم بعد موافقة المستخدم. بيخزّن لقطة من أعداد الواقف وقت
        /// الإقفال عشان التقرير يعرف يقول "اليوم ده اتقفل وفيه كذا مرحّل"
        /// حتى لو الدفعات اتحركت بعد كده.
        /// </summary>
        public async Task<ProductionDayClosure> CloseAsync(DateTime date, string? notes = null)
        {
            await using var transaction = await _unitOfWork.BeginWriteTransactionAsync();

            // التحقق جوه القفل: نسختين بيقفلوا نفس اليوم في نفس اللحظة
            // هيكسروا الفهرس الفريد، والرسالة دي أوضح من خطأ قاعدة بيانات
            if (await _closureRepo.IsClosedAsync(date))
                throw new InvalidOperationException($"إنتاج يوم {date:yyyy/MM/dd} مقفول بالفعل");

            var parked = await _batchService.GetAllParkedAsync(date);

            var closure = new ProductionDayClosure
            {
                Date = date.Date,
                ClosedAt = DateTime.Now,
                CarriedBatchCount = parked.Count,
                CarriedPieces = parked.Sum(l => l.Quantity),
                Notes = notes
            };

            await _closureRepo.AddAsync(closure);
            await _closureRepo.SaveChangesAsync();
            await transaction.CommitAsync();

            return closure;
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
