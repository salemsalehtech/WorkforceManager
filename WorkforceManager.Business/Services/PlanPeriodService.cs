using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// خطة بفترة مخصصة (PlanPeriod) — نفس فكرة MonthlyPlanService بالظبط
    /// (كمية مخططة لكل منتج، مرة واحدة لكل فترة) بس لفترة تاريخ حرة بدل
    /// شهر تقويمي. تخطيط بس — مفيش تحقيق ولا تتبع إنتاج فعلي هنا، ده
    /// فيتشر منفصل لاحق زي ما MonthlyPlan اتعمل بيها بالظبط (wfm-issue-25
    /// كانت تخطيط بس، والتتبع/Excel جم بعدين). شوف CLAUDE.md ليه الفترة
    /// المخصصة موازية لـMonthlyPlan مش استبدال ليها.
    /// </summary>
    public class PlanPeriodService
    {
        private readonly AppDbContext _db;

        public PlanPeriodService(AppDbContext db)
        {
            _db = db;
        }

        private async Task<HashSet<DateTime>> LoadHolidaySetAsync(DateTime start, DateTime end)
        {
            var holidays = await _db.MonthlyWorkCalendarHolidays
                .Where(h => h.Date >= start.Date && h.Date <= end.Date)
                .Select(h => h.Date.Date)
                .ToListAsync();
            return holidays.ToHashSet();
        }

        /// <summary>
        /// معاينة عدد أيام الشغل لفترة لسه ماتحفظتش — نفس حسبة
        /// CreatePeriodAsync بالظبط (شوف LoadHolidaySetAsync)، عشان الرقم
        /// المعروض في PlanPeriodDialog ما يختلفش أبدًا عن اللي بيتخزّن فعليًا.
        /// </summary>
        public async Task<int> PreviewWorkdaysAsync(DateTime start, DateTime end)
        {
            if (end.Date < start.Date)
                throw new ArgumentException("تاريخ النهاية لازم يكون بعد أو يساوي تاريخ البداية", nameof(end));

            var holidays = await LoadHolidaySetAsync(start, end);
            return WorkCalendarRules.TotalWorkdays(start, end, holidays);
        }

        /// <summary>
        /// بينشئ فترة جديدة ويحسب عدد أيام الشغل فيها مرة واحدة وقت
        /// الإنشاء (عطلة تتضاف بعد كده مابتحدّثش الرقم المخزّن تلقائيًا —
        /// شوف تعليق PlanPeriod.WorkdayCount).
        /// </summary>
        public async Task<PlanPeriod> CreatePeriodAsync(DateTime start, DateTime end)
        {
            if (end.Date < start.Date)
                throw new ArgumentException("تاريخ النهاية لازم يكون بعد أو يساوي تاريخ البداية", nameof(end));

            var holidays = await LoadHolidaySetAsync(start, end);
            var period = new PlanPeriod
            {
                StartDate = start.Date,
                EndDate = end.Date,
                WorkdayCount = WorkCalendarRules.TotalWorkdays(start, end, holidays),
            };

            _db.PlanPeriods.Add(period);
            await _db.SaveChangesAsync();
            return period;
        }

        /// <summary>كل الفترات، الأحدث (بتاريخ البداية) الأول — لقايمة الاختيار في الشاشة</summary>
        public Task<List<PlanPeriod>> GetPeriodsAsync() =>
            _db.PlanPeriods.OrderByDescending(p => p.StartDate).ToListAsync();

        /// <summary>
        /// كل المنتجات النشطة بخطة الفترة دي (0 لو لسه مفيش قيمة) — نفس
        /// شكل MonthlyPlanService.GetForMonthAsync بالظبط: استعلامين ثابتين
        /// مدموجين في الذاكرة، مش استعلام لكل منتج.
        /// </summary>
        public async Task<List<PlanPeriodProductDto>> GetForPeriodAsync(int periodId)
        {
            var products = await _db.Products
                .Where(p => p.IsActive)
                .Include(p => p.Family)
                .ToListAsync();

            var targets = await _db.PlanPeriodTargets
                .Where(t => t.PlanPeriodId == periodId)
                .ToDictionaryAsync(t => t.ProductId, t => t.PlannedQuantity);

            return products
                .OrderBy(p => p.Name)
                .Select(p => new PlanPeriodProductDto(
                    p.Id, p.Name, p.FamilyId, p.Family?.Name,
                    targets.GetValueOrDefault(p.Id), MonthlyPlanService.IsComplete(p)))
                .ToList();
        }

        public async Task SetTargetAsync(int periodId, int productId, int plannedQuantity)
        {
            if (plannedQuantity < 0)
                throw new ArgumentException("الكمية المخططة لازم تكون صفر أو أكتر", nameof(plannedQuantity));

            var existing = await _db.PlanPeriodTargets.FirstOrDefaultAsync(
                t => t.PlanPeriodId == periodId && t.ProductId == productId);

            if (existing is null)
            {
                _db.PlanPeriodTargets.Add(new PlanPeriodTarget
                {
                    PlanPeriodId = periodId, ProductId = productId, PlannedQuantity = plannedQuantity
                });
            }
            else
            {
                existing.PlannedQuantity = plannedQuantity;
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>الفترة دي فيها أي صف متسجل بالفعل؟ — الشاشة بتسأل تأكيد قبل النسخ لو آه</summary>
        public Task<bool> PeriodHasEntriesAsync(int periodId) =>
            _db.PlanPeriodTargets.AnyAsync(t => t.PlanPeriodId == periodId);

        /// <summary>
        /// ينسخ خطة أحدث فترة سابقة (بتاريخ البداية) كنقطة بداية لهذه الفترة
        /// — Upsert زي CopyFromPreviousMonthAsync بالظبط. بيرجّع 0 لو مفيش
        /// فترة سابقة أو مفيهاش صفوف.
        /// </summary>
        public async Task<int> CopyFromPreviousPeriodAsync(int periodId)
        {
            var current = await _db.PlanPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("الفترة غير موجودة");

            var previousPeriod = await _db.PlanPeriods
                .Where(p => p.StartDate < current.StartDate)
                .OrderByDescending(p => p.StartDate)
                .FirstOrDefaultAsync();

            if (previousPeriod is null) return 0;

            var previous = await _db.PlanPeriodTargets
                .Where(t => t.PlanPeriodId == previousPeriod.Id)
                .ToListAsync();

            if (previous.Count == 0) return 0;

            var currentTargets = await _db.PlanPeriodTargets
                .Where(t => t.PlanPeriodId == periodId)
                .ToDictionaryAsync(t => t.ProductId);

            foreach (var prev in previous)
            {
                if (currentTargets.TryGetValue(prev.ProductId, out var existing))
                    existing.PlannedQuantity = prev.PlannedQuantity;
                else
                    _db.PlanPeriodTargets.Add(new PlanPeriodTarget
                    {
                        PlanPeriodId = periodId, ProductId = prev.ProductId, PlannedQuantity = prev.PlannedQuantity
                    });
            }

            await _db.SaveChangesAsync();
            return previous.Count;
        }
    }
}
