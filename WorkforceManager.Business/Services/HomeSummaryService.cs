using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيجمّع أرقام شاشة الرئيسية من خدمات موجودة أصلاً — مفيش قاعدة عمل
    /// جديدة هنا، بس Sum/Count/Where بسيطة على نفس القوايم اللي الشاشات
    /// الحقيقية (العمال، المنتجات، الذاكرة، الحضور) بتقراها أصلاً،
    /// والاختيارات القليلة الخاصة بالرئيسية (الأقل أداءً، أقل منتج، خطط
    /// قريبة، سلسلة الالتزام) في HomeDashboardRules النقية. "رقم واحد،
    /// مصدر واحد" — لو رقم هنا مايطابقش اللي في الشاشة الأصلية، الخطأ في
    /// الشاشة دي مش في مصدر البيانات.
    ///
    /// كل استعلام هنا محدود بمدى تاريخ (أسبوعين للأرقام، ~67 يوم للسلسلة)
    /// — مفيش قراءة جدول كامل من نوع PendingWorkService.
    /// </summary>
    public class HomeSummaryService
    {
        /// <summary>
        /// رصيد أولي بيتعتبر "مفتوح من زمان" بعد قد إيه (من OriginalDate
        /// لليوم) — 7 أيام باتفاق مع المستخدم وقت تصميم شاشة الرئيسية.
        /// </summary>
        public const int StaleInitialBalanceDays = 7;

        /// <summary>
        /// أيام زيادة بنحمّلها قبل مدى السلسلة — وجود أي سجل حضور فيها معناه إن
        /// السلسلة أطول من المدى فعلاً ("60+")، مش إن البرنامج لسه جديد.
        /// أسبوع كامل عشان أجازة طويلة شوية على الحافة ماتضيّعش الإشارة.
        /// </summary>
        private const int StreakCapProbeDays = 7;

        private readonly WeeklySummaryService _weeklySummary;
        private readonly InitialBalanceService _initialBalances;
        private readonly ProductionMemoryService _productionMemory;
        private readonly ProductActivityService _productActivity;
        private readonly IAttendanceRepository _attendance;
        private readonly MonthlyPlanTrackingService _monthlyPlanTracking;

        public HomeSummaryService(
            WeeklySummaryService weeklySummary,
            InitialBalanceService initialBalances,
            ProductionMemoryService productionMemory,
            ProductActivityService productActivity,
            IAttendanceRepository attendance,
            MonthlyPlanTrackingService monthlyPlanTracking)
        {
            _weeklySummary = weeklySummary;
            _initialBalances = initialBalances;
            _productionMemory = productionMemory;
            _productActivity = productActivity;
            _attendance = attendance;
            _monthlyPlanTracking = monthlyPlanTracking;
        }

        public async Task<HomeSummaryDto> GetSummaryAsync(DateTime today)
        {
            var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(today);
            var (previousStart, previousEnd) = (weekStart.AddDays(-7), weekEnd.AddDays(-7));

            var team = await _weeklySummary.GetTeamSummaryForRangeAsync(weekStart, weekEnd);

            // نفس مدى الأسبوع بالظبط بس مزاح 7 أيام لورا — أساس كل أسهم المقارنة
            var previousWeekTeam = await _weeklySummary.GetTeamSummaryForRangeAsync(previousStart, previousEnd);
            var previousPiecesByWorker = previousWeekTeam.ToDictionary(s => s.WorkerId, s => s.TotalPieces);

            // الترتيب الكامل من غير قص لأول 3 — نفس لبنات "أسوأ عامل" في البحث السريع
            var difficulty = await _weeklySummary.LoadDifficultyByStageIdAsync();
            var ranked = WorkerRecognitionRules.Rank(team, difficulty);
            var worstWorker = HomeDashboardRules.PickWorstWorker(ranked);

            var bestWorker = team
                .Where(s => s.IsBestWorkerOfWeek)
                .OrderBy(s => s.RecognitionRank)
                .FirstOrDefault();

            var products = await _productActivity.GetAsync(weekStart, weekEnd);
            var previousProducts = (await _productActivity.GetAsync(previousStart, previousEnd))
                .ToDictionary(p => p.ProductId, p => p.CompletedPieces);

            HomeProductStat? ToStat(ProductActivityDto? p) => p is null
                ? null
                : new HomeProductStat(p.ProductId, p.ProductName, p.CompletedPieces,
                    previousProducts.GetValueOrDefault(p.ProductId));

            var totalPresent = team.Sum(s => s.PresentDays);
            var totalRecordedAttendance = team.Sum(s =>
                s.PresentDays + s.AbsentWithPermissionDays + s.AbsentWithoutPermissionDays);

            var allBalances = await _initialBalances.GetAllAsync();
            var staleBalances = allBalances
                .Where(b => b.Status != InitialBalanceStatus.Completed
                    && (today.Date - b.OriginalDate.Date).TotalDays >= StaleInitialBalanceDays)
                .ToList();

            var dueSoonMemories = HomeDashboardRules.DueSoon(await _productionMemory.GetActiveAsync(), today);

            // سجلات الحضور لمدى السلسلة + أسبوع زيادة (استعلام واحد محدود). الحسابات
            // الإدارية مستبعدة زي الملخص الأسبوعي بالظبط — حضورهم تلقائي ومش جزء من
            // "التزام العمال"
            var streakRecords = await _attendance.GetByRangeAsync(
                today.Date.AddDays(-(HomeDashboardRules.StreakLookbackDays - 1 + StreakCapProbeDays)), today.Date);
            var streak = HomeDashboardRules.ComputeStreak(
                streakRecords
                    .Where(a => a.Worker.HourlyRole is not (HourlyRole.DepartmentManager or HourlyRole.DepartmentHead))
                    .Select(a => (a.Date, a.Status)),
                today);

            // نفس تتبّع شاشة الخطة الشهرية بالظبط (MonthlyPlanTrackingService)،
            // مجمّع على مستوى كل المنتجات — مفيش صيغة تانية هنا
            var monthlyTracking = await _monthlyPlanTracking.GetTrackingAsync(today.Year, today.Month, today);
            var withPlan = monthlyTracking.Where(p => p.PlannedQuantity > 0).ToList();
            var totalProRatedPlan = withPlan.Sum(p => p.ProRatedPlan);
            var monthlyPlanAchievedPercent = totalProRatedPlan > 0
                ? withPlan.Sum(p => p.EffectiveAchieved) / totalProRatedPlan
                : (decimal?)null;

            return new HomeSummaryDto
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                TotalPiecesThisWeek = team.Sum(s => s.TotalPieces),
                ActiveWorkersThisWeek = CountActive(team),
                NetWorkdaysThisWeek = team.Sum(s => s.NetWorkdays),
                BestWorkerOfWeek = bestWorker,
                BestWorkerPreviousPieces = bestWorker is null ? 0 : previousPiecesByWorker.GetValueOrDefault(bestWorker.WorkerId),
                WorstWorkerOfWeek = worstWorker,
                WorstWorkerPreviousPieces = worstWorker is null ? 0 : previousPiecesByWorker.GetValueOrDefault(worstWorker.WorkerId),
                StaleInitialBalances = staleBalances,
                DueSoonMemories = dueSoonMemories,
                PreviousWeekTotalPieces = previousWeekTeam.Sum(s => s.TotalPieces),
                PreviousWeekActiveWorkers = CountActive(previousWeekTeam),
                PreviousWeekNetWorkdays = previousWeekTeam.Sum(s => s.NetWorkdays),
                UnexcusedAbsencesThisWeek = team.Sum(s => s.AbsentWithoutPermissionDays),
                PreviousWeekUnexcusedAbsences = previousWeekTeam.Sum(s => s.AbsentWithoutPermissionDays),
                TopProduct = ToStat(HomeDashboardRules.PickTopProduct(products)),
                BottomProduct = ToStat(HomeDashboardRules.PickBottomProduct(products)),
                StreakDays = streak.Days,
                StreakIsCapped = streak.IsCapped,
                AttendanceRatePercent = totalRecordedAttendance > 0
                    ? Math.Round(100m * totalPresent / totalRecordedAttendance, 0)
                    : null,
                MonthlyPlanAchievedPercent = monthlyPlanAchievedPercent
            };
        }

        // "نشط" = أنتج قطع أو اشتغل بالساعة — تعريف واحد للأسبوعين عشان المقارنة تبقى زي بزي
        private static int CountActive(IEnumerable<WorkerWeeklySummaryDto> team) =>
            team.Count(s => s.ProducedWorkdays > 0 || s.HourlyDaysWorked > 0);
    }
}
