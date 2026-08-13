using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قلب الحسابات الأسبوعية كلها. أسبوع الشغل بيبدأ الخميس وينتهي
    /// الأربع (متفق عليه مع مدير القسم)، وكل أسبوع بيبدأ عداد يوميات
    /// جديد — لكن كل السجلات القديمة محفوظة زي ما هي، لأن الملخص بيتحسب
    /// من السجلات الفعلية (إنتاج/حضور/جزاءات) وقت الطلب مش من عداد مخزّن.
    ///
    /// قواعد الخصم:
    /// - غياب بإذن: محايد تمامًا، مفيش أي خصم.
    /// - غياب بدون إذن: خصم نص يومية (0.5) عن كل يوم غياب.
    /// - الجزاءات: بتتخصم بقيمتها (نص يوم / يوم / 3 أيام / أسبوع = 6 يوميات).
    /// الصافي = المنتَج − خصم الغياب − خصم الجزاءات، وده اللي بيتحدد بيه
    /// أحسن عامل في الأسبوع وبيظهر في تقرير كل عامل.
    /// </summary>
    public class WeeklySummaryService
    {
        // قاعدة خصم الغياب اتنقلت لـ AbsenceDeductionRule (مكان واحد مشترك
        // مع PayrollService) عشان الخصم ميتحسبش مرتين بعد جزاء الغياب التلقائي

        private readonly IDailyProductionRepository _productionRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly IPenaltyRepository _penaltyRepo;
        private readonly IWorkerRepository _workerRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;

        public WeeklySummaryService(
            IDailyProductionRepository productionRepo,
            IAttendanceRepository attendanceRepo,
            IPenaltyRepository penaltyRepo,
            IWorkerRepository workerRepo,
            IHourlyWorkLogRepository hourlyRepo)
        {
            _productionRepo = productionRepo;
            _attendanceRepo = attendanceRepo;
            _penaltyRepo = penaltyRepo;
            _workerRepo = workerRepo;
            _hourlyRepo = hourlyRepo;
        }

        /// <summary>
        /// يحسب بداية ونهاية أسبوع الشغل (الخميس → الأربع) اللي بيقع
        /// فيه التاريخ المُعطى. مثال: أي يوم من خميس 9/7 لأربع 15/7
        /// يرجع (9/7 ، 15/7).
        /// </summary>
        public static (DateTime WeekStart, DateTime WeekEnd) GetWorkWeekRange(DateTime anyDate)
        {
            var date = anyDate.Date;
            // عدد الأيام اللي فاتت من آخر يوم خميس: الخميس نفسه = 0، الجمعة = 1 ... الأربع = 6
            int daysSinceThursday = ((int)date.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
            var weekStart = date.AddDays(-daysSinceThursday);
            return (weekStart, weekStart.AddDays(6));
        }

        /// <summary>
        /// يبني الملخص الأسبوعي الكامل لكل العمال النشطين في الأسبوع اللي
        /// بيقع فيه التاريخ المعطى، مرتبين تنازليًا بصافي اليوميات، مع
        /// تحديد أحسن عامل في الأسبوع (أعلى صافي، بشرط إنه أنتج فعلاً).
        /// </summary>
        public async Task<List<WorkerWeeklySummaryDto>> GetTeamWeeklySummaryAsync(DateTime anyDateInWeek)
        {
            var (weekStart, weekEnd) = GetWorkWeekRange(anyDateInWeek);

            // تحميل كل بيانات الأسبوع مرة واحدة (4 استعلامات بس مهما كان عدد العمال)
            var production = await _productionRepo.GetByRangeAsync(weekStart, weekEnd);
            var attendance = await _attendanceRepo.GetByRangeAsync(weekStart, weekEnd);
            var penalties = await _penaltyRepo.GetByRangeAsync(weekStart, weekEnd);
            var hourly = await _hourlyRepo.GetByRangeAsync(weekStart, weekEnd);

            return BuildTeamSummaryForWeek(weekStart, weekEnd, production, attendance, penalties, hourly);
        }

        /// <summary>
        /// الملخص الأسبوعي لعامل واحد بس (لبروفايله وتقريره الفردي).
        /// بيرجع null لو العامل مالوش أي نشاط في الأسبوع ده.
        /// </summary>
        public async Task<WorkerWeeklySummaryDto?> GetWorkerWeeklySummaryAsync(int workerId, DateTime anyDateInWeek)
        {
            var team = await GetTeamWeeklySummaryAsync(anyDateInWeek);
            return team.FirstOrDefault(s => s.WorkerId == workerId);
        }

        /// <summary>
        /// الهستوري الأسبوعي الكامل لعامل: ملخص كل أسبوع من "from" لحد "to"،
        /// الأحدث أولًا — ده اللي بيظهر في بروفايل العامل كسجل تاريخي،
        /// والقديم كله محفوظ لأنه بيتحسب من السجلات الدائمة.
        ///
        /// الأداء: بنحمّل بيانات كل الفريق للفترة كلها مرة واحدة (3 استعلامات
        /// فقط مهما كان عدد الأسابيع)، وبنقسّمها على الأسابيع في الذاكرة —
        /// بدل ما نعمل استعلامات منفصلة لكل أسبوع. تحميل الفريق (مش العامل
        /// وحده) ضروري عشان نحدد "أحسن عامل" في كل أسبوع صح.
        /// </summary>
        public async Task<List<WorkerWeeklySummaryDto>> GetWorkerWeeklyHistoryAsync(int workerId, DateTime from, DateTime to)
        {
            var (firstWeekStart, _) = GetWorkWeekRange(from);
            var (lastWeekStart, lastWeekEnd) = GetWorkWeekRange(to);

            // تحميل الفترة كلها دفعة واحدة (4 استعلامات إجمالًا)
            var production = await _productionRepo.GetByRangeAsync(firstWeekStart, lastWeekEnd);
            var attendance = await _attendanceRepo.GetByRangeAsync(firstWeekStart, lastWeekEnd);
            var penalties = await _penaltyRepo.GetByRangeAsync(firstWeekStart, lastWeekEnd);
            var hourly = await _hourlyRepo.GetByRangeAsync(firstWeekStart, lastWeekEnd);

            // تقسيم كل نوع بيانات على أسابيعه (بمفتاح = تاريخ بداية الأسبوع = الخميس)
            var productionByWeek = production.ToLookup(p => GetWorkWeekRange(p.Date).WeekStart);
            var attendanceByWeek = attendance.ToLookup(a => GetWorkWeekRange(a.Date).WeekStart);
            var penaltiesByWeek = penalties.ToLookup(p => GetWorkWeekRange(p.Date).WeekStart);
            var hourlyByWeek = hourly.ToLookup(h => GetWorkWeekRange(h.Date).WeekStart);

            var history = new List<WorkerWeeklySummaryDto>();
            for (var cursor = firstWeekStart; cursor <= lastWeekStart; cursor = cursor.AddDays(7))
            {
                var team = BuildTeamSummaryForWeek(
                    cursor, cursor.AddDays(6),
                    productionByWeek[cursor], attendanceByWeek[cursor], penaltiesByWeek[cursor], hourlyByWeek[cursor]);

                // بناخد سطر العامل المطلوب بس من ملخص الأسبوع (لو ليه نشاط فيه)
                var mine = team.FirstOrDefault(s => s.WorkerId == workerId);
                if (mine is not null)
                    history.Add(mine);
            }

            // الأحدث الأول — أول حاجة المدير عايز يشوفها هي آخر أسبوع
            history.Reverse();
            return history;
        }

        /// <summary>
        /// يبني ملخص أسبوع كامل لكل العمال من قوائم مُفلترة مسبقًا لنفس الأسبوع،
        /// مرتّبين بصافي اليوميات، مع تحديد أحسن عامل. كل الحسابات في الذاكرة
        /// (من غير أي استعلام) — نقطة الاستخدام المشتركة بين الملخص الأسبوعي
        /// والهستوري عشان القاعدة الحسابية تفضل مكان واحد.
        /// </summary>
        private static List<WorkerWeeklySummaryDto> BuildTeamSummaryForWeek(
            DateTime weekStart, DateTime weekEnd,
            IEnumerable<DailyProduction> production,
            IEnumerable<Attendance> attendance,
            IEnumerable<Penalty> penalties,
            IEnumerable<HourlyWorkLog> hourly)
        {
            var productionByWorker = production.ToLookup(p => p.WorkerId);
            var attendanceByWorker = attendance.ToLookup(a => a.WorkerId);
            var penaltiesByWorker = penalties.ToLookup(p => p.WorkerId);
            var hourlyByWorker = hourly.ToLookup(h => h.WorkerId);

            // كل العمال اللي ليهم أي نشاط في الأسبوع (إنتاج أو حضور أو جزاء أو شغل بالساعة)
            var workerIds = productionByWorker.Select(g => g.Key)
                .Concat(attendanceByWorker.Select(g => g.Key))
                .Concat(penaltiesByWorker.Select(g => g.Key))
                .Concat(hourlyByWorker.Select(g => g.Key))
                .Distinct();

            var summaries = new List<WorkerWeeklySummaryDto>();
            foreach (var workerId in workerIds)
            {
                var workerProduction = productionByWorker[workerId].ToList();
                var workerAttendance = attendanceByWorker[workerId].ToList();
                var workerPenalties = penaltiesByWorker[workerId].ToList();
                var workerHourly = hourlyByWorker[workerId].ToList();

                // اسم العامل من أي سجل متاح (كلهم محمّلين بـ Include للـ Worker)
                var workerRef = workerProduction.FirstOrDefault()?.Worker
                    ?? workerAttendance.FirstOrDefault()?.Worker
                    ?? workerHourly.FirstOrDefault()?.Worker
                    ?? workerPenalties.First().Worker;

                // الحسابات الإدارية (مدير/رئيس قسم) بتتحسب حاضر ويومية
                // تلقائيًا كل يوم (DepartmentAttendanceService)، بس الملخص
                // الأسبوعي وأحسن عامل الأسبوع تقارير عمال — مفيش خلط
                if (workerRef.HourlyRole is HourlyRole.DepartmentManager or HourlyRole.DepartmentHead) continue;

                summaries.Add(BuildWorkerSummary(
                    workerId, workerRef, weekStart, weekEnd,
                    workerProduction, workerAttendance, workerPenalties, workerHourly));
            }

            // الترتيب بصافي اليوميات (بعد كل الخصومات) — ده أساس تقييم الأسبوع
            var ordered = summaries.OrderByDescending(s => s.NetWorkdays).ToList();

            // أحسن عامل في الأسبوع = أعلى صافي، بشرط إن صافيه موجب وأنتج فعلاً
            // (عشان أسبوع كله غياب وجزاءات ميطلعش فيه "أحسن عامل" بصافي صفر أو سالب)
            var best = ordered.FirstOrDefault(s => s.ProducedWorkdays > 0 && s.NetWorkdays > 0);
            if (best is not null)
                best.IsBestWorkerOfWeek = true;

            return ordered;
        }

        /// <summary>يبني ملخص عامل واحد من سجلاته المُفلترة لأسبوع معين</summary>
        private static WorkerWeeklySummaryDto BuildWorkerSummary(
            int workerId, Worker workerRef, DateTime weekStart, DateTime weekEnd,
            List<DailyProduction> workerProduction,
            List<Attendance> workerAttendance,
            List<Penalty> workerPenalties,
            List<HourlyWorkLog> workerHourly)
        {
            var absentWithoutPermission = workerAttendance.Count(a => a.Status == AttendanceStatus.AbsentWithoutPermission);

            // يوميات الشغل بالساعة (للعمال بالساعة) — بتتجمع مع يوميات الإنتاج
            // في ProducedWorkdays عشان تدخل في الصافي والأجر زي أي يوميات
            var hourlyWorkdays = workerHourly.Sum(h => h.WorkdaysCredited);

            return new WorkerWeeklySummaryDto
            {
                WorkerId = workerId,
                WorkerName = workerRef.FullName,
                WeekStart = weekStart,
                WeekEnd = weekEnd,

                IsHourly = workerRef.IsHourly,
                HourlyDaysWorked = workerHourly.Count,
                HourlyWorkdays = hourlyWorkdays,
                DailyWageEgp = workerRef.DailyWageEgp,

                ProducedWorkdays = workerProduction.Sum(p => p.WorkdaysCompleted) + hourlyWorkdays,
                TotalPieces = workerProduction.Sum(p => p.PieceCount),
                // تفصيل الإنتاج مجمّع حسب المرحلة (نفس المرحلة ممكن تتكرر في كذا يوم خلال الأسبوع)
                Breakdown = workerProduction
                    .GroupBy(p => p.ProductionStageId)
                    .Select(g => new StageBreakdownDto
                    {
                        ProductName = g.First().ProductionStage.Product.Name,
                        StageName = g.First().ProductionStage.StageName,
                        PieceCount = g.Sum(p => p.PieceCount),
                        PiecesPerWorkday = g.First().PiecesPerWorkdayAtEntry
                    })
                    .ToList(),

                PresentDays = workerAttendance.Count(a => a.Status == AttendanceStatus.Present),
                AbsentWithPermissionDays = workerAttendance.Count(a => a.Status == AttendanceStatus.AbsentWithPermission),
                AbsentWithoutPermissionDays = absentWithoutPermission,
                // نص يومية عن كل يوم غياب بدون إذن — بس الأيام اللي ليها جزاء
                // تلقائي بتتخصم من خلال الجزاء نفسه، فمبتتعدّش هنا تاني
                // (القاعدة المشتركة في AbsenceDeductionRule)
                AbsenceDeduction = AbsenceDeductionRule.ComputeUnpenalizedAbsenceDeduction(
                    workerAttendance, workerPenalties),

                Penalties = workerPenalties.Select(p => new PenaltySummaryDto
                {
                    PenaltyId = p.Id,
                    Date = p.Date,
                    Reason = p.Reason,
                    Deduction = p.Deduction
                }).ToList(),
                PenaltyDeduction = workerPenalties.Sum(p => p.DeductedWorkdays)
            };
        }
    }
}
