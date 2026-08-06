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
    /// مفيش حاجة بتترحّل ولا بتتنقل: كل يوم بيتقرا من سجلات الإنتاج بتاعته
    /// لوحده، والشغل اللي لسه ما خلصش بيتسجل عادي في اليوم اللي هيتعمل فيه.
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
        /// رسالة رفض الكتابة على يوم مقفول — نص واحد لكل المسارات
        /// (رحلة إنتاج، سجل واحد، تعديل عدد قطع). المستخدم لازم يشوف
        /// نفس الجملة مهما كان جاي من فين.
        /// </summary>
        public static string ClosedDayMessage(DateTime date) =>
            $"إنتاج يوم {date:yyyy/MM/dd} مقفول — افتح اليوم تاني من شاشة التسجيل لو محتاج تعدّل";

        /// <summary>
        /// معاينة الإقفال: دخل الخط كام وخلص كام النهارده، لكل منتج.
        /// دي اللي بتتعرض للمستخدم قبل ما يوافق — مفيش يوم بيتقفل من غير
        /// ما يشوف أرقامه.
        /// </summary>
        public async Task<DayClosurePreviewDto> PreviewAsync(DateTime date)
        {
            var report = await _reportService.GetAsync(date);

            return new DayClosurePreviewDto
            {
                Date = date.Date,
                AlreadyClosed = await _closureRepo.IsClosedAsync(date),
                CompletedPieces = report.TotalCompletedPieces,
                StartedPieces = report.TotalStartedPieces,
                ByProduct = report.Products
                    .Select(p => new ProductOutputDto
                    {
                        ProductName = p.ProductName,
                        CompletedPieces = p.CompletedPieces,
                        StartedPieces = p.StartedPieces
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// يقفل اليوم بعد موافقة المستخدم. بيخزّن لقطة من أرقام اليوم عشان
        /// التقرير يعرف يقول "اليوم ده اتقفل بالأرقام دي" حتى لو حد صحّح
        /// سجل قديم بعد كده.
        /// </summary>
        public async Task<ProductionDayClosure> CloseAsync(DateTime date)
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
                StartedPieces = preview.StartedPieces
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
