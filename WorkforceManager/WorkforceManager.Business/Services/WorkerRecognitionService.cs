using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// ألقاب "أحسن عامل" الرسمية — منفصلة تمامًا عن
    /// WorkerWeeklySummaryDto.IsBestWorkerOfWeek اللحظي (شوف تعليق
    /// WorkerPerformanceTitle). المسؤولية هنا حاجتين: حساب مين يستاهل
    /// اللقب في فترة معينة (ComputeWeeklyTopAsync/ComputeMonthlyTopAsync)،
    /// وتسجيل الألقاب تلقائيًا لما فترة تقفل فعليًا (AwardTitlesForClosedPeriodsAsync
    /// — بتتنادى من App.OnStartup، زي DepartmentAttendanceService.EnsureDailyPresenceAsync
    /// بالظبط).
    ///
    /// **مفيش قفل يدوي وقفل الفترة بيتحدد بالتاريخ بس**: أول ما أسبوع/شهر
    /// جديد يبدأ (يتعرف من AppSettingsStore)، الفترة اللي فاتت بتتحسب
    /// وتتسجّل تلقائيًا في أول تشغيل بعدها.
    /// </summary>
    public class WorkerRecognitionService
    {
        /// <summary>حد أقصى للفترات اللي بتتحسب دفعة واحدة بعد انقطاع طويل للبرنامج</summary>
        private const int MaxWeeksToBackfill = 8;
        private const int MaxMonthsToBackfill = 3;

        private readonly WeeklySummaryService _weekly;
        private readonly IGenericRepository<WorkerPerformanceTitle> _titles;

        public WorkerRecognitionService(WeeklySummaryService weekly, IGenericRepository<WorkerPerformanceTitle> titles)
        {
            _weekly = weekly;
            _titles = titles;
        }

        /// <summary>أحسن 3 عمال في الأسبوع اللي بيقع فيه التاريخ المُعطى، بترتيبهم الحقيقي 1/2/3</summary>
        public async Task<List<WorkerWeeklySummaryDto>> ComputeWeeklyTopAsync(DateTime weekStart)
        {
            var team = await _weekly.GetTeamWeeklySummaryAsync(weekStart);
            return team.Where(s => s.IsBestWorkerOfWeek).OrderBy(s => s.RecognitionRank).ToList();
        }

        /// <summary>
        /// شرح كامل لسبب ترتيب عامل معيّن في أسبوع معيّن — أساس نافذة "ليه
        /// فاز؟". بيرجّع null لو العامل مش من ضمن المؤهلين للمقارنة أصلًا
        /// هذا الأسبوع (عامل ساعة، أو مفيش إنتاج/صافي غير موجب) — مش بس
        /// لغير الفايزين بالتلاتة، عشان لو الشاشة احتاجت تشرح ترتيب عامل
        /// مش من ضمن التلاتة الأولى تقدر كمان.
        /// </summary>
        public async Task<WorkerRecognitionExplanationDto?> GetWeeklyExplanationAsync(int workerId, DateTime anyDateInWeek)
        {
            var team = await _weekly.GetTeamWeeklySummaryAsync(anyDateInWeek);
            var difficultyByStageId = await _weekly.LoadDifficultyByStageIdAsync();
            var eligible = WorkerRecognitionRules.Rank(team, difficultyByStageId);

            var rankIndex = eligible.FindIndex(s => s.WorkerId == workerId);
            if (rankIndex < 0) return null;

            var summary = eligible[rankIndex];
            var breakdown = WorkerRecognitionRules.Explain(summary, difficultyByStageId);

            return new WorkerRecognitionExplanationDto
            {
                WorkerId = summary.WorkerId,
                WorkerName = summary.WorkerName,
                WeekStart = summary.WeekStart,
                WeekEnd = summary.WeekEnd,
                Rank = rankIndex + 1,
                EligibleWorkerCount = eligible.Count,
                TotalPieces = summary.TotalPieces,
                Breakdown = summary.Breakdown,
                DistinctStageCount = breakdown.DistinctStageCount,
                DiversityFactor = breakdown.DiversityFactor,
                AdjustedWorkdays = breakdown.AdjustedWorkdays,
                PresentDays = summary.PresentDays,
                AbsentWithPermissionDays = summary.AbsentWithPermissionDays,
                AbsentWithoutPermissionDays = summary.AbsentWithoutPermissionDays,
                AbsenceDeduction = summary.AbsenceDeduction,
                Penalties = summary.Penalties,
                PenaltyDeduction = summary.PenaltyDeduction,
                FinalScore = breakdown.FinalScore
            };
        }

        /// <summary>أحسن عامل واحد في الشهر اللي بيقع فيه التاريخ المُعطى</summary>
        public async Task<WorkerWeeklySummaryDto?> ComputeMonthlyTopAsync(DateTime monthStart)
        {
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var team = await _weekly.GetTeamSummaryForRangeAsync(monthStart, monthEnd);
            var difficultyByStageId = await _weekly.LoadDifficultyByStageIdAsync();
            return WorkerRecognitionRules.Rank(team, difficultyByStageId).FirstOrDefault();
        }

        /// <summary>
        /// بتتنادى مرة كل بدء تشغيل. بتسجّل لقب لكل أسبوع/شهر **قفل فعليًا**
        /// من آخر مرة اتحسبت (بحد أقصى معقول عشان انقطاع طويل للبرنامج ما
        /// يعملش حساب ثقيل). أول تشغيل (المؤشر null) بيبدأ من الفترة
        /// الحالية من غير ما يرجع لتاريخ قديم — شوف تعليق
        /// LastBestWorkerWeekComputedFor.
        /// </summary>
        /// <param name="settingsPath">
        /// مسار ملف الإعدادات — null يعني المسار الحقيقي (AppPaths.SettingsPath).
        /// موجود بس عشان الاختبارات تقدر تعزل نفسها عن settings.json
        /// الحقيقي بتاع أي نسخة شغالة على نفس الجهاز؛ الاستدعاء الحقيقي من
        /// App.OnStartup ما بيمررش حاجة هنا.
        /// </param>
        public async Task AwardTitlesForClosedPeriodsAsync(string? settingsPath = null)
        {
            var settings = AppSettingsStore.Load(settingsPath);
            var today = DateTime.Today;
            var (currentWeekStart, _) = WeeklySummaryService.GetWorkWeekRange(today);
            var currentMonthStart = new DateTime(today.Year, today.Month, 1);

            if (settings.LastBestWorkerWeekComputedFor is null)
            {
                settings.LastBestWorkerWeekComputedFor = currentWeekStart;
                AppSettingsStore.Save(settings, settingsPath);
            }
            else
            {
                var cursor = settings.LastBestWorkerWeekComputedFor.Value.AddDays(7);
                for (var i = 0; cursor < currentWeekStart && i < MaxWeeksToBackfill; i++, cursor = cursor.AddDays(7))
                {
                    await AwardWeeklyTitlesAsync(cursor);
                    settings.LastBestWorkerWeekComputedFor = cursor;
                    AppSettingsStore.Save(settings, settingsPath);
                }
            }

            if (settings.LastBestWorkerMonthComputedFor is null)
            {
                settings.LastBestWorkerMonthComputedFor = currentMonthStart;
                AppSettingsStore.Save(settings, settingsPath);
            }
            else
            {
                var cursor = settings.LastBestWorkerMonthComputedFor.Value.AddMonths(1);
                for (var i = 0; cursor < currentMonthStart && i < MaxMonthsToBackfill; i++, cursor = cursor.AddMonths(1))
                {
                    await AwardMonthlyTitleAsync(cursor);
                    settings.LastBestWorkerMonthComputedFor = cursor;
                    AppSettingsStore.Save(settings, settingsPath);
                }
            }
        }

        private async Task AwardWeeklyTitlesAsync(DateTime weekStart)
        {
            var winners = await ComputeWeeklyTopAsync(weekStart);
            if (winners.Count == 0) return;

            foreach (var winner in winners)
            {
                await _titles.AddAsync(new WorkerPerformanceTitle
                {
                    WorkerId = winner.WorkerId,
                    TitleType = PerformanceTitleType.WeeklyTop3,
                    PeriodStart = weekStart,
                    PeriodEnd = weekStart.AddDays(6)
                });
            }
            await _titles.SaveChangesAsync();
        }

        private async Task AwardMonthlyTitleAsync(DateTime monthStart)
        {
            var winner = await ComputeMonthlyTopAsync(monthStart);
            if (winner is null) return;

            await _titles.AddAsync(new WorkerPerformanceTitle
            {
                WorkerId = winner.WorkerId,
                TitleType = PerformanceTitleType.MonthlyBest,
                PeriodStart = monthStart,
                PeriodEnd = monthStart.AddMonths(1).AddDays(-1)
            });
            await _titles.SaveChangesAsync();
        }

        /// <summary>
        /// أحدث لقب أسبوعي (لحد 3 عمال) وأحدث لقب شهري (عامل واحد) —
        /// دي الشارات اللي بتفضل ثابتة على بروفايل العامل لحد ما حد
        /// يكسبها منه.
        /// </summary>
        public async Task<List<WorkerPerformanceTitle>> GetCurrentTitleHoldersAsync()
        {
            var all = await _titles.GetAllAsync();
            var result = new List<WorkerPerformanceTitle>();

            var latestWeekly = all
                .Where(t => t.TitleType == PerformanceTitleType.WeeklyTop3)
                .Select(t => (DateTime?)t.PeriodStart)
                .Max();
            if (latestWeekly is not null)
                result.AddRange(all.Where(t => t.TitleType == PerformanceTitleType.WeeklyTop3 && t.PeriodStart == latestWeekly));

            var latestMonthly = all
                .Where(t => t.TitleType == PerformanceTitleType.MonthlyBest)
                .Select(t => (DateTime?)t.PeriodStart)
                .Max();
            if (latestMonthly is not null)
                result.AddRange(all.Where(t => t.TitleType == PerformanceTitleType.MonthlyBest && t.PeriodStart == latestMonthly));

            return result;
        }
    }
}
