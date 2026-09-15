using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيجمّع أرقام شاشة الرئيسية من تلات خدمات موجودة أصلاً — مفيش
    /// استعلام جديد ولا قاعدة عمل جديدة هنا، بس Sum/Count/Where بسيطة
    /// على نفس القوائم اللي الشاشات الحقيقية (العمال، المنتجات، الذاكرة)
    /// بتقراها أصلاً. "رقم واحد، مصدر واحد" — لو رقم هنا مايطابقش اللي
    /// في الشاشة الأصلية، الخطأ في الشاشة دي مش في مصدر البيانات.
    /// </summary>
    public class HomeSummaryService
    {
        /// <summary>
        /// رصيد أولي بيتعتبر "مفتوح من زمان" بعد قد إيه (من OriginalDate
        /// لليوم) — 7 أيام باتفاق مع المستخدم وقت تصميم شاشة الرئيسية.
        /// </summary>
        public const int StaleInitialBalanceDays = 7;

        private readonly WeeklySummaryService _weeklySummary;
        private readonly InitialBalanceService _initialBalances;
        private readonly ProductionMemoryService _productionMemory;
        private readonly ProductActivityService _productActivity;

        public HomeSummaryService(
            WeeklySummaryService weeklySummary,
            InitialBalanceService initialBalances,
            ProductionMemoryService productionMemory,
            ProductActivityService productActivity)
        {
            _weeklySummary = weeklySummary;
            _initialBalances = initialBalances;
            _productionMemory = productionMemory;
            _productActivity = productActivity;
        }

        public async Task<HomeSummaryDto> GetSummaryAsync(DateTime today)
        {
            var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(today);
            var team = await _weeklySummary.GetTeamSummaryForRangeAsync(weekStart, weekEnd);

            // نفس مدى الأسبوع بالظبط بس مزاح 7 أيام لورا — لعرض نسبة التغيير في الشاشة
            var previousWeekTeam = await _weeklySummary.GetTeamSummaryForRangeAsync(
                weekStart.AddDays(-7), weekEnd.AddDays(-7));

            var products = await _productActivity.GetAsync(weekStart, weekEnd);
            var topProduct = products
                .Where(p => p.WorkedInPeriod)
                .OrderByDescending(p => p.CompletedPieces)
                .FirstOrDefault();

            var totalPresent = team.Sum(s => s.PresentDays);
            var totalRecordedAttendance = team.Sum(s =>
                s.PresentDays + s.AbsentWithPermissionDays + s.AbsentWithoutPermissionDays);

            var allBalances = await _initialBalances.GetAllAsync();
            var staleBalances = allBalances
                .Where(b => b.Status != InitialBalanceStatus.Completed
                    && (today.Date - b.OriginalDate.Date).TotalDays >= StaleInitialBalanceDays)
                .ToList();

            var dueMemories = await _productionMemory.GetDueAsync(today);

            return new HomeSummaryDto
            {
                WeekStart = weekStart,
                WeekEnd = weekEnd,
                TotalPiecesThisWeek = team.Sum(s => s.TotalPieces),
                ActiveWorkersThisWeek = team.Count(s => s.ProducedWorkdays > 0 || s.HourlyDaysWorked > 0),
                NetWorkdaysThisWeek = team.Sum(s => s.NetWorkdays),
                BestWorkerOfWeek = team
                    .Where(s => s.IsBestWorkerOfWeek)
                    .OrderBy(s => s.RecognitionRank)
                    .FirstOrDefault(),
                StaleInitialBalances = staleBalances,
                DueMemoriesCount = dueMemories.Count,
                PreviousWeekTotalPieces = previousWeekTeam.Sum(s => s.TotalPieces),
                TopProductName = topProduct?.ProductName,
                TopProductPieces = topProduct?.CompletedPieces ?? 0,
                AttendanceRatePercent = totalRecordedAttendance > 0
                    ? Math.Round(100m * totalPresent / totalRecordedAttendance, 0)
                    : null
            };
        }
    }
}
