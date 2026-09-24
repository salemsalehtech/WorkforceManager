using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تتبّع الخطة الشهرية — المحقق/نسبة المحقق/المطلوب يوميًا/التوقّع لكل
    /// منتج. **كل رقم هنا مبني فوق خدمات موجودة، مفيش حساب "تام" مزدوج**:
    /// المحقق من DailyProductionReportService.GetForRangeAsync بالظبط
    /// (نفس تعريف "تام" الموثّق فيها)، التصليحات من MonthlyPlanCorrection
    /// اليدوي الصرف (مالهوش أي علاقة بـDailyProduction.IsRework/عمال
    /// الإعادة)، الخطة من MonthlyPlan.
    ///
    /// **الاستعلامات محدودة بعدد أيام المدى، مش بعدد المنتجات** —
    /// GetForRangeAsync بتعمل استعلامين لكل يوم في المدى (نمط موجود من
    /// قبل الفيتشر ده)، مش استعلام لكل منتج؛ على شهر كامل (~26 يوم شغل)
    /// ده ~52 استعلام SQLite بسيطة، مش N×عدد المنتجات.
    /// </summary>
    public class MonthlyPlanTrackingService
    {
        /// <summary>هامش الإيقاع "ماشي صح" — أقل من 90% متأخر، أكتر من 110% سابق</summary>
        private const decimal OnTrackLowerBound = 0.90m;
        private const decimal OnTrackUpperBound = 1.10m;

        private readonly AppDbContext _db;
        private readonly DailyProductionReportService _dailyReport;

        public MonthlyPlanTrackingService(AppDbContext db, DailyProductionReportService dailyReport)
        {
            _db = db;
            _dailyReport = dailyReport;
        }

        public async Task<List<MonthlyPlanTrackingDto>> GetTrackingAsync(int year, int month, DateTime asOfDate)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var effectiveAsOf = asOfDate.Date > monthEnd ? monthEnd : asOfDate.Date;

            var holidays = await LoadHolidaySetAsync(year, month);
            var totalWorkdays = WorkCalendarRules.TotalWorkdays(year, month, holidays);
            var elapsedWorkdays = WorkCalendarRules.ElapsedWorkdays(year, month, effectiveAsOf, holidays);
            var remainingWorkdays = totalWorkdays - elapsedWorkdays;

            // المحقق من أول الشهر لحد اليوم المطلوب — استعلام واحد بيغطي المدى كله (يوم بيوم داخليًا، مش منتج بمنتج)
            var achievedRange = await _dailyReport.GetForRangeAsync(monthStart, effectiveAsOf);
            var achievedByProduct = achievedRange.Products.ToDictionary(p => p.ProductId, p => p.CompletedPieces);

            // إنتاج النهارده بس (رقم واحد، مش استعلام إضافي — effectiveAsOf غالبًا النهارده)
            var todayReport = await _dailyReport.GetAsync(effectiveAsOf);
            var todayByProduct = todayReport.Products.ToDictionary(p => p.ProductId, p => p.CompletedPieces);

            // نفس المدى من الشهر اللي فات — نفس منطق مقارنة الفترات في ProductionChartService (إزاحة شهر، مفيش صيغة جديدة)
            var (prevYear, prevMonth) = month == 1 ? (year - 1, 12) : (year, month - 1);
            var prevMonthStart = new DateTime(prevYear, prevMonth, 1);
            var prevMonthEnd = prevMonthStart.AddMonths(1).AddDays(-1);
            var prevEffectiveAsOf = new DateTime(prevYear, prevMonth, Math.Min(effectiveAsOf.Day, DateTime.DaysInMonth(prevYear, prevMonth)));
            var previousRange = prevEffectiveAsOf > prevMonthEnd ? null : await _dailyReport.GetForRangeAsync(prevMonthStart, prevEffectiveAsOf);
            var previousByProduct = previousRange?.Products.ToDictionary(p => p.ProductId, p => p.CompletedPieces)
                ?? new Dictionary<int, int>();

            var corrections = await _db.MonthlyPlanCorrections
                .Where(c => c.Date >= monthStart && c.Date <= effectiveAsOf)
                .GroupBy(c => c.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(c => c.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            var plans = await _db.MonthlyPlans
                .Where(mp => mp.Year == year && mp.Month == month)
                .ToDictionaryAsync(mp => mp.ProductId, mp => mp.PlannedQuantity);

            var products = await _db.Products
                .Where(p => p.IsActive)
                .Include(p => p.Family)
                .ToListAsync();

            // منتج له نشاط في المدى بس مفيش له صف Product نشط (نادر — اتوقف بعد التسجيل) لسه لازم يبان "خارج الخطة"
            var relevantProductIds = products.Select(p => p.Id)
                .Union(achievedByProduct.Keys)
                .Union(plans.Keys)
                .ToHashSet();

            var productById = products.ToDictionary(p => p.Id);
            var result = new List<MonthlyPlanTrackingDto>();

            foreach (var productId in relevantProductIds)
            {
                var plannedQuantity = plans.GetValueOrDefault(productId);
                var achieved = achievedByProduct.GetValueOrDefault(productId);
                var correctionTotal = corrections.GetValueOrDefault(productId);
                var effectiveAchieved = achieved + correctionTotal;

                var proRatedPlan = totalWorkdays == 0
                    ? 0m
                    : (decimal)plannedQuantity * elapsedWorkdays / totalWorkdays;

                decimal? achievedPercent = proRatedPlan > 0 ? effectiveAchieved / proRatedPlan : null;

                var isOutsidePlan = plannedQuantity == 0 && effectiveAchieved > 0;

                var status = achievedPercent switch
                {
                    null => isOutsidePlan ? PlanPaceStatus.Ahead : PlanPaceStatus.OnTrack,
                    < OnTrackLowerBound => PlanPaceStatus.Behind,
                    > OnTrackUpperBound => PlanPaceStatus.Ahead,
                    _ => PlanPaceStatus.OnTrack
                };

                int? requiredDailyOutput = remainingWorkdays > 0
                    ? (int)Math.Max(0, Math.Ceiling((plannedQuantity - effectiveAchieved) / (decimal)remainingWorkdays))
                    : null;

                int? forecastEndOfMonth = elapsedWorkdays > 0
                    ? (int)Math.Round(effectiveAchieved * (decimal)totalWorkdays / elapsedWorkdays)
                    : null;

                var product = productById.GetValueOrDefault(productId);

                result.Add(new MonthlyPlanTrackingDto(
                    ProductId: productId,
                    ProductName: product?.Name ?? achievedRange.Products.FirstOrDefault(p => p.ProductId == productId)?.ProductName ?? "",
                    FamilyId: product?.FamilyId,
                    FamilyName: product?.Family?.Name,
                    IsComplete: product is not null && MonthlyPlanService.IsComplete(product),
                    PieceWeightGrams: product?.PieceWeightGrams,
                    Material: product?.Material,
                    PlannedQuantity: plannedQuantity,
                    AchievedToDate: achieved,
                    CorrectionsToDate: correctionTotal,
                    TodayCompleted: todayByProduct.GetValueOrDefault(productId),
                    EffectiveAchieved: effectiveAchieved,
                    ProRatedPlan: proRatedPlan,
                    AchievedPercent: achievedPercent,
                    Status: status,
                    RequiredDailyOutput: requiredDailyOutput,
                    ForecastEndOfMonth: forecastEndOfMonth,
                    SameDayPreviousMonth: previousByProduct.GetValueOrDefault(productId),
                    IsOutsidePlan: isOutsidePlan));
            }

            return result.OrderBy(r => r.ProductName).ToList();
        }

        private async Task<HashSet<DateTime>> LoadHolidaySetAsync(int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var holidays = await _db.MonthlyWorkCalendarHolidays
                .Where(h => h.Date >= monthStart && h.Date <= monthEnd)
                .Select(h => h.Date.Date)
                .ToListAsync();
            return holidays.ToHashSet();
        }

        // ======================= تصليحات =======================

        /// <summary>يضيف/يعدّل تصليح يوم واحد لمنتج واحد — Upsert، مايأثرش على أي يوم تاني</summary>
        public async Task SetCorrectionAsync(int productId, DateTime date, int quantity, string? notes = null)
        {
            var day = date.Date;
            var existing = await _db.MonthlyPlanCorrections
                .FirstOrDefaultAsync(c => c.ProductId == productId && c.Date == day);

            if (existing is null)
            {
                _db.MonthlyPlanCorrections.Add(new Core.Models.MonthlyPlanCorrection
                { ProductId = productId, Date = day, Quantity = quantity, Notes = notes });
            }
            else
            {
                existing.Quantity = quantity;
                existing.Notes = notes;
            }

            await _db.SaveChangesAsync();
        }

        // ======================= عطلة يدوية =======================

        public async Task AddHolidayAsync(DateTime date, string? reason = null)
        {
            var day = date.Date;
            if (await _db.MonthlyWorkCalendarHolidays.AnyAsync(h => h.Date == day))
                throw new InvalidOperationException("اليوم ده متسجل عطلة بالفعل");

            _db.MonthlyWorkCalendarHolidays.Add(new Core.Models.MonthlyWorkCalendarHoliday { Date = day, Reason = reason });
            await _db.SaveChangesAsync();
        }

        public async Task RemoveHolidayAsync(int holidayId)
        {
            var holiday = await _db.MonthlyWorkCalendarHolidays.FindAsync(holidayId);
            if (holiday is null) return;
            _db.MonthlyWorkCalendarHolidays.Remove(holiday);
            await _db.SaveChangesAsync();
        }

        public async Task<List<Core.Models.MonthlyWorkCalendarHoliday>> GetHolidaysAsync(int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            return await _db.MonthlyWorkCalendarHolidays
                .Where(h => h.Date >= monthStart && h.Date <= monthEnd)
                .OrderBy(h => h.Date)
                .ToListAsync();
        }
    }
}
