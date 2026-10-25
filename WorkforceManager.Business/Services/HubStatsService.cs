using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الأرقام الحية اللي بتتعرض على كروت صفحات "إدارة العمال" و"تسجيل الإنتاج اليومي".
    /// مفيش حساب جديد هنا — كل رقم عدّ بسيط على بيانات يوم واحد من نفس الخدمات والمستودعات
    /// اللي الشاشات نفسها بتستخدمها، فالكارت والشاشة اللي وراه مايختلفوش.
    /// </summary>
    public class HubStatsService
    {
        private readonly IWorkerRepository _workerRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IPenaltyRepository _penaltyRepo;
        private readonly IWageAdjustmentRepository _adjustmentRepo;
        private readonly IDailyProductionRepository _productionRepo;
        private readonly InitialBalanceService _balances;
        private readonly ProductionMemoryService _memory;
        private readonly ScrapService _scrap;
        private readonly IProductRepository _productRepo;
        private readonly HomeSummaryService _home;

        public HubStatsService(
            IWorkerRepository workerRepo,
            IAttendanceRepository attendanceRepo,
            IPenaltyRepository penaltyRepo,
            IWageAdjustmentRepository adjustmentRepo,
            IDailyProductionRepository productionRepo,
            InitialBalanceService balances,
            ProductionMemoryService memory,
            ScrapService scrap,
            IProductRepository productRepo,
            HomeSummaryService home)
        {
            _workerRepo = workerRepo;
            _attendanceRepo = attendanceRepo;
            _penaltyRepo = penaltyRepo;
            _adjustmentRepo = adjustmentRepo;
            _productionRepo = productionRepo;
            _balances = balances;
            _memory = memory;
            _scrap = scrap;
            _productRepo = productRepo;
            _home = home;
        }

        public async Task<WorkersHubStatsDto> GetWorkersStatsAsync(DateTime date)
        {
            var day = date.Date;

            var active = (await _workerRepo.FindAsync(w => w.IsActive)).Count;
            var attendance = await _attendanceRepo.GetByDateAsync(day);
            var penalties = await _penaltyRepo.GetByRangeAsync(day, day);
            var adjustments = await _adjustmentRepo.GetByDateAsync(day);

            return new WorkersHubStatsDto(
                active,
                attendance.Count(a => a.Status == AttendanceStatus.Present),
                attendance.Count(a => a.IsAbsence),
                penalties.Count,
                adjustments.Count);
        }

        public async Task<ProductionHubStatsDto> GetProductionStatsAsync(DateTime date)
        {
            var day = date.Date;

            var products = (await _productRepo.FindAsync(p => p.IsActive)).Count;
            var production = await _productionRepo.GetByDateAsync(day);

            // نفس رقم بلاطة الخطة الشهرية في الرئيسية (null = مفيش خطة الشهر ده لسه)
            var home = await _home.GetSummaryAsync(day);

            return new ProductionHubStatsDto(products, home.MonthlyPlanAchievedPercent, production.Count);
        }

        public async Task<DailyEntryHubStatsDto> GetDailyEntryStatsAsync(DateTime date)
        {
            var day = date.Date;

            var production = await _productionRepo.GetByDateAsync(day);
            var balances = await _balances.GetAllAsync();
            var due = await _memory.GetDueAsync(day);
            var scrap = await _scrap.GetByDateAsync(day);

            return new DailyEntryHubStatsDto(
                production.Count,
                balances.Count(b => b.RemainingQuantity > 0),
                due.Count,
                scrap.Sum(s => s.PieceCount));
        }
    }
}
