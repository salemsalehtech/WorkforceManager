using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تتبّع الخطة الشهرية — المحقق/نسبة المحقق/المطلوب يوميًا/التوقّع لكل
    /// منتج. **المحقق دلوقتي رقم يدوي صرف (MonthlyPlanDailyEntry)، مش
    /// مشتق من DailyProductionReportService** — الإنتاج الحقيقي بقى بس
    /// مرجع اختياري (زرار "استخدم نفس الرقم الحقيقي" + تحذير الاختلاف في
    /// تبويب "الإنتاج اليومي")، شوف CLAUDE.md ليه الاستبدال ده حصل.
    /// التصليحات لسه من MonthlyPlanCorrection اليدوي الصرف (دلتا فوق
    /// المحقق، مش بديل ليه)، الخطة من MonthlyPlan.
    ///
    /// **الاستعلامات محدودة بعدد أيام المدى، مش بعدد المنتجات** — نفس
    /// نمط GetForRangeAsync القديم، بس بقى استعلام واحد GROUP BY على جدول
    /// المحقق اليدوي بدل استعلامين لكل يوم (أرخص من القديم، مش أغلى).
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

        /// <summary>مجموع "الإنتاج اليومي" اليدوي لكل منتج في مدى تاريخ — استعلام واحد GROUP BY، مش لكل يوم</summary>
        private async Task<Dictionary<int, int>> LoadDailyEntryTotalsAsync(DateTime from, DateTime to) =>
            await _db.MonthlyPlanDailyEntries
                .Where(e => e.Date >= from.Date && e.Date <= to.Date)
                .GroupBy(e => e.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(e => e.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

        public async Task<List<MonthlyPlanTrackingDto>> GetTrackingAsync(int year, int month, DateTime asOfDate)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var effectiveAsOf = asOfDate.Date > monthEnd ? monthEnd : asOfDate.Date;

            var holidays = await LoadHolidaySetAsync(year, month);
            var totalWorkdays = WorkCalendarRules.TotalWorkdays(year, month, holidays);
            var elapsedWorkdays = WorkCalendarRules.ElapsedWorkdays(year, month, effectiveAsOf, holidays);
            var remainingWorkdays = totalWorkdays - elapsedWorkdays;

            // المحقق اليدوي من أول الشهر لحد اليوم المطلوب
            var achievedByProduct = await LoadDailyEntryTotalsAsync(monthStart, effectiveAsOf);

            // إنتاج النهارده بس (نفس جدول المحقق اليدوي، مدى يوم واحد)
            var todayByProduct = await LoadDailyEntryTotalsAsync(effectiveAsOf, effectiveAsOf);

            // نفس المدى من الشهر اللي فات — يدوي مقابل يدوي (مش حقيقي مقابل يدوي، عشان المقارنة تفضل ذات معنى)
            var (prevYear, prevMonth) = month == 1 ? (year - 1, 12) : (year, month - 1);
            var prevMonthStart = new DateTime(prevYear, prevMonth, 1);
            var prevMonthEnd = prevMonthStart.AddMonths(1).AddDays(-1);
            var prevEffectiveAsOf = new DateTime(prevYear, prevMonth, Math.Min(effectiveAsOf.Day, DateTime.DaysInMonth(prevYear, prevMonth)));
            var previousByProduct = prevEffectiveAsOf > prevMonthEnd
                ? new Dictionary<int, int>()
                : await LoadDailyEntryTotalsAsync(prevMonthStart, prevEffectiveAsOf);

            var corrections = await _db.MonthlyPlanCorrections
                .Where(c => c.Date >= monthStart && c.Date <= effectiveAsOf)
                .GroupBy(c => c.ProductId)
                .Select(g => new { ProductId = g.Key, Total = g.Sum(c => c.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Total);

            var plans = await _db.MonthlyPlans
                .Where(mp => mp.Year == year && mp.Month == month)
                .ToDictionaryAsync(mp => mp.ProductId, mp => (mp.PlannedQuantity, mp.DailyTargetQuantity));

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

            // نادر: منتج اتوقف في نص الشهر لسه له إدخال محقق قديم — لسه لازم يبان "خارج الخطة"، فمحتاج اسمه من برّه القايمة النشطة
            var missingIds = relevantProductIds.Except(productById.Keys).ToList();
            var fallbackNames = missingIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Products.Where(p => missingIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);

            var result = new List<MonthlyPlanTrackingDto>();

            foreach (var productId in relevantProductIds)
            {
                var (plannedQuantity, dailyTarget) = plans.GetValueOrDefault(productId);
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
                    ProductName: product?.Name ?? fallbackNames.GetValueOrDefault(productId, ""),
                    FamilyId: product?.FamilyId,
                    FamilyName: product?.Family?.Name,
                    IsComplete: product is not null && MonthlyPlanService.IsComplete(product),
                    PieceWeightGrams: product?.PieceWeightGrams,
                    Material: product?.Material,
                    PlannedQuantity: plannedQuantity,
                    DailyTargetQuantity: dailyTarget,
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

        /// <summary>
        /// تصليح يوم واحد لمنتج واحد — 0 لو مفيش صف. بيُستخدم من شاشة
        /// تسجيل الإنتاج اليومي (مكان الإدخال الحالي) عشان تظهر القيمة
        /// المتسجلة لو المستخدم رجع لنفس اليوم/المنتج.
        /// </summary>
        public async Task<int> GetCorrectionAsync(int productId, DateTime date)
        {
            var day = date.Date;
            var existing = await _db.MonthlyPlanCorrections
                .FirstOrDefaultAsync(c => c.ProductId == productId && c.Date == day);
            return existing?.Quantity ?? 0;
        }

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

        // ======================= الإنتاج اليومي (يدوي، بديل المحقق التلقائي) =======================

        /// <summary>إدخال يوم واحد لمنتج واحد — 0 لو مفيش صف بعد</summary>
        public async Task<int> GetDailyEntryAsync(int productId, DateTime date)
        {
            var day = date.Date;
            var existing = await _db.MonthlyPlanDailyEntries
                .FirstOrDefaultAsync(e => e.ProductId == productId && e.Date == day);
            return existing?.Quantity ?? 0;
        }

        /// <summary>يضيف/يعدّل إدخال يوم واحد لمنتج واحد — Upsert، مايأثرش على أي يوم تاني</summary>
        public async Task SetDailyEntryAsync(int productId, DateTime date, int quantity)
        {
            if (quantity < 0)
                throw new ArgumentException("الكمية لازم تكون صفر أو أكتر", nameof(quantity));

            var day = date.Date;
            var existing = await _db.MonthlyPlanDailyEntries
                .FirstOrDefaultAsync(e => e.ProductId == productId && e.Date == day);

            if (existing is null)
                _db.MonthlyPlanDailyEntries.Add(new Core.Models.MonthlyPlanDailyEntry
                { ProductId = productId, Date = day, Quantity = quantity });
            else
                existing.Quantity = quantity;

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// كل المنتجات النشطة لتبويب "الإنتاج اليومي" — دايمًا عن
        /// <paramref name="today"/> الحقيقي، مش أي تاريخ تاني. استعلامين
        /// بس (اليوم + أمبارح على جدول المحقق اليدوي) + استعلام واحد على
        /// الإنتاج الحقيقي (DailyProductionReportService.GetAsync، بيغطي
        /// كل المنتجات في نداء واحد) — مفيش استعلام لكل منتج خالص.
        /// </summary>
        public async Task<List<MonthlyPlanTodayEntryDto>> GetTodayEntryTabAsync(DateTime today)
        {
            var day = today.Date;
            var yesterday = day.AddDays(-1);

            var todayEntries = await _db.MonthlyPlanDailyEntries
                .Where(e => e.Date == day)
                .ToDictionaryAsync(e => e.ProductId, e => e.Quantity);

            var yesterdayEntries = await _db.MonthlyPlanDailyEntries
                .Where(e => e.Date == yesterday)
                .ToDictionaryAsync(e => e.ProductId, e => e.Quantity);

            var realToday = await _dailyReport.GetAsync(day);
            var realByProduct = realToday.Products.ToDictionary(p => p.ProductId, p => p.CompletedPieces);

            var products = await _db.Products
                .Where(p => p.IsActive)
                .Include(p => p.Family)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return products.Select(p => new MonthlyPlanTodayEntryDto(
                ProductId: p.Id,
                ProductName: p.Name,
                FamilyId: p.FamilyId,
                FamilyName: p.Family?.Name,
                ManualQuantity: todayEntries.GetValueOrDefault(p.Id),
                IsFilled: todayEntries.ContainsKey(p.Id),
                RealProductionToday: realByProduct.GetValueOrDefault(p.Id),
                YesterdayQuantity: yesterdayEntries.GetValueOrDefault(p.Id)))
                .ToList();
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
