using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقارير الإنتاج عن فترة زمنية يحددها المستخدم (يوم/أسبوع/شهر أو
    /// مدى مخصص من تاريخ لتاريخ):
    /// - تقرير عام: ملخص إجمالي للقسم + تفصيل بالمنتج/المرحلة + بالعامل.
    /// - تقرير عامل معين: إنتاجه بالتفصيل وباليوم + حضوره + أجره وجزاءاته.
    ///
    /// بيجمّع كل الأيام في المدى مباشرة (مش أسابيع كاملة) عشان أي مدى
    /// ينفع. القطع المكتملة = المسجلة على آخر مرحلة لكل منتج (زي الرسم
    /// البياني) — عشان مجموع كل المراحل ميعدّش نفس القطعة أكتر من مرة.
    /// </summary>
    public class ProductionReportService
    {
        private const decimal UnexcusedAbsenceDeductionPerDay = 0.5m;

        private readonly IDailyProductionRepository _productionRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IPenaltyRepository _penaltyRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly IProductRepository _productRepo;
        private readonly IWorkerRepository _workerRepo;
        private readonly IWageAdjustmentRepository _adjustmentRepo;

        public ProductionReportService(
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo,
            IPenaltyRepository penaltyRepo,
            IHourlyWorkLogRepository hourlyRepo,
            IProductRepository productRepo,
            IWorkerRepository workerRepo,
            IWageAdjustmentRepository adjustmentRepo)
        {
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
            _penaltyRepo = penaltyRepo;
            _hourlyRepo = hourlyRepo;
            _productRepo = productRepo;
            _workerRepo = workerRepo;
            _adjustmentRepo = adjustmentRepo;
        }

        /// <summary>آخر مرحلة (أعلى ترتيب) لكل منتج — لتحديد القطع المكتملة</summary>
        private async Task<HashSet<int>> GetLastStageIdsAsync()
        {
            var products = await _productRepo.GetAllWithStagesAsync();
            return products
                .Where(p => p.Stages.Count > 0)
                .Select(p => (p.Stages.Any(s => s.IsActive) ? p.Stages.Where(s => s.IsActive) : p.Stages)
                    .OrderByDescending(s => s.SortOrder).ThenByDescending(s => s.Id).First().Id)
                .ToHashSet();
        }

        /// <summary>التقرير العام للإنتاج عن فترة [from, to]</summary>
        public async Task<GeneralProductionReportDto> GetGeneralReportAsync(DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate = to.Date;

            var production = await _productionRepo.GetByRangeAsync(fromDate, toDate);
            var hourly = await _hourlyRepo.GetByRangeAsync(fromDate, toDate);
            var lastStageIds = await GetLastStageIdsAsync();

            // تفصيل بالمنتج/المرحلة
            var byProductStage = production
                .GroupBy(r => r.ProductionStageId)
                .Select(g =>
                {
                    var stage = g.First().ProductionStage;
                    return new ProductStageProductionDto
                    {
                        ProductName = stage.Product.Name,
                        StageName = stage.StageName,
                        SortOrder = stage.SortOrder,
                        Pieces = g.Sum(r => r.PieceCount),
                        Workdays = g.Sum(r => r.WorkdaysCompleted),
                        IsLastStage = lastStageIds.Contains(g.Key)
                    };
                })
                .OrderBy(x => x.ProductName).ThenBy(x => x.SortOrder)
                .ToList();

            // تفصيل بالعامل (إنتاج + شغل ساعة)
            var hourlyByWorker = hourly.ToLookup(h => h.WorkerId);
            var byWorker = production
                .GroupBy(r => r.WorkerId)
                .Select(g =>
                {
                    var worker = g.First().Worker;
                    var wh = hourlyByWorker[g.Key];
                    return new WorkerProductionSummaryDto
                    {
                        WorkerId = g.Key,
                        WorkerName = worker.FullName,
                        IsHourly = worker.IsHourly,
                        TotalPieces = g.Sum(r => r.PieceCount),
                        TotalWorkdays = g.Sum(r => r.WorkdaysCompleted) + wh.Sum(h => h.WorkdaysCredited)
                    };
                })
                .ToList();

            // ضم العمال بالساعة اللي مالهمش إنتاج قطع بس ليهم شغل ساعة
            var workersWithProduction = byWorker.Select(w => w.WorkerId).ToHashSet();
            foreach (var g in hourlyByWorker.Where(g => !workersWithProduction.Contains(g.Key)))
            {
                var worker = g.First().Worker;
                byWorker.Add(new WorkerProductionSummaryDto
                {
                    WorkerId = g.Key,
                    WorkerName = worker.FullName,
                    IsHourly = worker.IsHourly,
                    TotalPieces = 0,
                    TotalWorkdays = g.Sum(h => h.WorkdaysCredited)
                });
            }
            byWorker = byWorker.OrderByDescending(w => w.TotalWorkdays).ToList();

            // الملخص الإجمالي
            var completedPieces = production
                .Where(r => lastStageIds.Contains(r.ProductionStageId))
                .Sum(r => r.PieceCount);
            var totalWorkdays = production.Sum(r => r.WorkdaysCompleted) + hourly.Sum(h => h.WorkdaysCredited);
            var productionDays = production.Select(r => r.Date.Date)
                .Concat(hourly.Select(h => h.Date.Date)).Distinct().Count();

            return new GeneralProductionReportDto
            {
                From = fromDate,
                To = toDate,
                TotalCompletedPieces = completedPieces,
                TotalWorkdays = totalWorkdays,
                WorkersCount = byWorker.Count,
                ProductionDays = productionDays,
                ByProductStage = byProductStage,
                ByWorker = byWorker
            };
        }

        /// <summary>تقرير عامل معين عن فترة [from, to]</summary>
        public async Task<WorkerProductionReportDto> GetWorkerReportAsync(int workerId, DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate = to.Date;

            var worker = await _workerRepo.GetByIdAsync(workerId)
                ?? throw new InvalidOperationException("العامل المحدد غير موجود");

            var production = (await _productionRepo.GetByWorkerAndRangeAsync(workerId, fromDate, toDate)).ToList();
            var attendance = (await _attendanceRepo.GetByWorkerAndRangeAsync(workerId, fromDate, toDate)).ToList();
            var penalties = (await _penaltyRepo.GetByWorkerAndRangeAsync(workerId, fromDate, toDate)).ToList();
            var hourly = (await _hourlyRepo.GetByRangeAsync(fromDate, toDate))
                .Where(h => h.WorkerId == workerId).ToList();
            var adjustments = (await _adjustmentRepo.GetByWorkerAndRangeAsync(workerId, fromDate, toDate)).ToList();
            var lastStageIds = await GetLastStageIdsAsync();

            // الإنتاج بالمنتج/المرحلة
            var byProductStage = production
                .GroupBy(r => r.ProductionStageId)
                .Select(g =>
                {
                    var stage = g.First().ProductionStage;
                    return new ProductStageProductionDto
                    {
                        ProductName = stage.Product.Name,
                        StageName = stage.StageName,
                        SortOrder = stage.SortOrder,
                        Pieces = g.Sum(r => r.PieceCount),
                        Workdays = g.Sum(r => r.WorkdaysCompleted),
                        IsLastStage = lastStageIds.Contains(g.Key)
                    };
                })
                .OrderBy(x => x.ProductName).ThenBy(x => x.SortOrder)
                .ToList();

            // الإنتاج باليوم (إنتاج + شغل ساعة)
            var byDay = production
                .GroupBy(r => r.Date.Date)
                .Select(g => new WorkerDayProductionDto
                {
                    Date = g.Key,
                    Pieces = g.Sum(r => r.PieceCount),
                    Workdays = g.Sum(r => r.WorkdaysCompleted),
                    Detail = string.Join("، ", g
                        .GroupBy(r => r.ProductionStageId)
                        .Select(sg => $"{sg.First().ProductionStage.Product.Name}/{sg.First().ProductionStage.StageName}: {sg.Sum(r => r.PieceCount)}"))
                })
                .ToList();

            // أيام شغل الساعة (اللي مالهاش إنتاج قطع في نفس اليوم)
            var daysWithProduction = byDay.Select(d => d.Date).ToHashSet();
            foreach (var h in hourly.Where(h => !daysWithProduction.Contains(h.Date.Date)))
            {
                byDay.Add(new WorkerDayProductionDto
                {
                    Date = h.Date.Date,
                    Pieces = 0,
                    Workdays = h.WorkdaysCredited,
                    Detail = $"شغل بالساعة ({worker.HourlyRole?.ToArabicName()})"
                });
            }
            byDay = byDay.OrderBy(d => d.Date).ToList();

            var absentWithoutPermission = attendance.Count(a => a.Status == AttendanceStatus.AbsentWithoutPermission);
            var producedWorkdays = production.Sum(r => r.WorkdaysCompleted) + hourly.Sum(h => h.WorkdaysCredited);

            return new WorkerProductionReportDto
            {
                WorkerId = workerId,
                WorkerName = worker.FullName,
                IsHourly = worker.IsHourly,
                TypeText = worker.IsHourly ? $"بالساعة ({worker.HourlyRole?.ToArabicName()})" : "إنتاج بالقطعة",
                DailyWageEgp = worker.DailyWageEgp,
                From = fromDate,
                To = toDate,
                TotalPieces = production.Sum(r => r.PieceCount),
                ProducedWorkdays = producedWorkdays,
                ByProductStage = byProductStage,
                ByDay = byDay,
                PresentDays = attendance.Count(a => a.Status == AttendanceStatus.Present),
                AbsentWithPermissionDays = attendance.Count(a => a.Status == AttendanceStatus.AbsentWithPermission),
                AbsentWithoutPermissionDays = absentWithoutPermission,
                AbsenceDeduction = absentWithoutPermission * UnexcusedAbsenceDeductionPerDay,
                PenaltyDeduction = penalties.Sum(p => p.DeductedWorkdays),
                Penalties = penalties.Select(p => new PenaltySummaryDto
                {
                    PenaltyId = p.Id,
                    Date = p.Date,
                    Reason = p.Reason,
                    Deduction = p.Deduction
                }).ToList(),
                BonusEgp = adjustments.Where(a => a.Type == WageAdjustmentType.Bonus).Sum(a => a.AmountEgp),
                AdvanceEgp = adjustments.Where(a => a.Type == WageAdjustmentType.Advance).Sum(a => a.AmountEgp),
                Adjustments = adjustments.Select(a => new WageAdjustmentSummaryDto
                {
                    AdjustmentId = a.Id,
                    Date = a.Date,
                    Type = a.Type,
                    AmountEgp = a.AmountEgp,
                    Note = a.Note
                }).ToList()
            };
        }
    }
}
