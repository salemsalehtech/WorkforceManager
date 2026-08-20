using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// كشف أجور فترة زمنية مخصصة (شهر مثلاً). بيجمّع كل الأيام في المدى
    /// مباشرة — مش أسابيع كاملة زي WeeklySummaryService — عشان المدير
    /// يختار من تاريخ لتاريخ ويطلع إجمالي أجر كل عامل بالجنيه.
    ///
    /// قواعد الحساب: أجر اليوميات = صافي اليوميات × سعر اليومية، والصافي =
    /// (إنتاج + شغل بالساعة) − خصم الغياب بدون إذن (نص يومية/يوم) −
    /// خصم الجزاءات. الأجر النهائي = أجر اليوميات + الحوافز − السلف.
    /// السعر الحالي للعامل بيُطبّق على كل الفترة.
    /// </summary>
    public class PayrollService
    {
        // قاعدة خصم الغياب في AbsenceDeductionRule (مشتركة مع WeeklySummaryService)

        private readonly IDailyProductionRepository _productionRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IPenaltyRepository _penaltyRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly IWageAdjustmentRepository _adjustmentRepo;

        public PayrollService(
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo,
            IPenaltyRepository penaltyRepo,
            IHourlyWorkLogRepository hourlyRepo,
            IWageAdjustmentRepository adjustmentRepo)
        {
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
            _penaltyRepo = penaltyRepo;
            _hourlyRepo = hourlyRepo;
            _adjustmentRepo = adjustmentRepo;
        }

        /// <summary>يبني كشف أجور كل العمال اللي لهم نشاط في الفترة [from, to]</summary>
        public async Task<PeriodPayrollDto> GetPeriodPayrollAsync(DateTime from, DateTime to)
        {
            var fromDate = from.Date;
            var toDate = to.Date;

            // تحميل كل بيانات الفترة مرة واحدة (4 استعلامات)
            var production = await _productionRepo.GetByRangeAsync(fromDate, toDate);
            var attendance = await _attendanceRepo.GetByRangeAsync(fromDate, toDate);
            var penalties = await _penaltyRepo.GetByRangeAsync(fromDate, toDate);
            var hourly = await _hourlyRepo.GetByRangeAsync(fromDate, toDate);
            var adjustments = await _adjustmentRepo.GetByRangeAsync(fromDate, toDate);

            var productionByWorker = production.ToLookup(p => p.WorkerId);
            var attendanceByWorker = attendance.ToLookup(a => a.WorkerId);
            var penaltiesByWorker = penalties.ToLookup(p => p.WorkerId);
            var hourlyByWorker = hourly.ToLookup(h => h.WorkerId);
            var adjustmentsByWorker = adjustments.ToLookup(a => a.WorkerId);

            var workerIds = productionByWorker.Select(g => g.Key)
                .Concat(attendanceByWorker.Select(g => g.Key))
                .Concat(penaltiesByWorker.Select(g => g.Key))
                .Concat(hourlyByWorker.Select(g => g.Key))
                .Concat(adjustmentsByWorker.Select(g => g.Key))
                .Distinct();

            var workers = new List<WorkerPayrollDto>();
            foreach (var workerId in workerIds)
            {
                var wp = productionByWorker[workerId].ToList();
                var wa = attendanceByWorker[workerId].ToList();
                var wpen = penaltiesByWorker[workerId].ToList();
                var wh = hourlyByWorker[workerId].ToList();
                var wadj = adjustmentsByWorker[workerId].ToList();

                // بيانات العامل (اسم + سعر) من أي سجل محمّل بـ Include للـ Worker
                var workerRef = wp.FirstOrDefault()?.Worker
                    ?? wa.FirstOrDefault()?.Worker
                    ?? wh.FirstOrDefault()?.Worker
                    ?? wpen.FirstOrDefault()?.Worker
                    ?? wadj.First().Worker;

                // الحسابات الإدارية (مدير/رئيس قسم) بتتحسب حاضر ويومية
                // تلقائيًا كل يوم (DepartmentAttendanceService)، بس كشف
                // الأجور ده تقرير عمال — مفيش خلط أبدًا، شوف تقريرهم
                // المنفصل (ReportSubject.DepartmentAccounts)
                if (workerRef.HourlyRole is HourlyRole.DepartmentManager or HourlyRole.DepartmentHead) continue;

                var producedWorkdays = wp.Sum(p => p.WorkdaysCompleted) + wh.Sum(h => h.WorkdaysCredited);

                // عدد أيام العمل الفعلية = أيام فيها إنتاج أو شغل ساعة (بدون تكرار)
                var workDays = wp.Select(p => p.Date.Date)
                    .Concat(wh.Select(h => h.Date.Date))
                    .Distinct()
                    .Count();

                workers.Add(new WorkerPayrollDto
                {
                    WorkerId = workerId,
                    WorkerName = workerRef.FullName,
                    IsHourly = workerRef.IsHourly,
                    SortOrder = workerRef.SortOrder,
                    DailyWageEgp = workerRef.DailyWageEgp,
                    TotalPieces = wp.Sum(p => p.PieceCount),

                    // الأكتر شغلًا الأول: لو المساحة ضاقت، اللي بيتقص
                    // هو الأقل أهمية للعامل
                    StageBreakdown = wp
                        .Where(r => r.ProductionStage?.Product is not null)
                        .GroupBy(r => r.ProductionStageId)
                        .Select(g => new WorkerStageWorkDto
                        {
                            ProductName = g.First().ProductionStage.Product.Name,
                            StageName = g.First().ProductionStage.StageName,
                            Pieces = g.Sum(r => r.PieceCount)
                        })
                        .OrderByDescending(s => s.Pieces)
                        .ToList(),
                    ProducedWorkdays = producedWorkdays,
                    // نفس قاعدة WeeklySummaryService بالحرف: أيام الغياب اللي ليها
                    // جزاء تلقائي بتتخصم من خلال الجزاء، فمبتتعدّش هنا كمان
                    AbsenceDeduction = AbsenceDeductionRule.ComputeUnpenalizedAbsenceDeduction(wa, wpen),
                    PenaltyDeduction = wpen.Sum(p => p.DeductedWorkdays),
                    DaysWorked = workDays,
                    BonusEgp = wadj.Where(a => a.Type == WageAdjustmentType.Bonus).Sum(a => a.AmountEgp),
                    AdvanceEgp = wadj.Where(a => a.Type == WageAdjustmentType.Advance).Sum(a => a.AmountEgp)
                });
            }

            return new PeriodPayrollDto
            {
                From = fromDate,
                To = toDate,
                Workers = workers.OrderByDescending(w => w.NetWageEgp).ToList()
            };
        }
    }
}
