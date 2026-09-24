using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// خطة الإنتاج الشهرية — كمية مخططة لكل منتج، لكل شهر (مرة واحدة بس،
    /// شوف الفهرس الفريد على MonthlyPlan في AppDbContext).
    ///
    /// **مفيش رقم "خطة عيلة" بيتحسب أو يتخزن هنا خالص** — خطة العيلة على
    /// الشاشة دايمًا SUM لخطط منتجاتها، وده مسؤولية الـViewModel (نفس
    /// تجميع شاشة المنتجات بالعيلة) مش هنا؛ هنا بيرجّع قايمة مسطّحة بس.
    /// هذه الشاشة **تخطيط بس** — مفيش تحقيق ولا مقارنة إنتاج فعلي هنا،
    /// ده فيتشر منفصل لاحق (wfm-issue-26).
    /// </summary>
    public class MonthlyPlanService
    {
        private readonly AppDbContext _db;

        public MonthlyPlanService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>منتج "مكتمل البيانات" لغرض الخطة الشهرية: عنده وزن قطعة ومادة. العيلة اختيارية ومش شرط.</summary>
        public static bool IsComplete(Product p) => p.PieceWeightGrams is not null && p.Material is not null;

        /// <summary>
        /// كل المنتجات النشطة بخطة الشهر ده (0 لو لسه مفيش قيمة) — استعلامين
        /// ثابتين (منتجات+عائلاتهم، وخطط الشهر المحدد بس) مدموجين في
        /// الذاكرة، مش استعلام لكل منتج.
        /// </summary>
        public async Task<List<MonthlyPlanProductDto>> GetForMonthAsync(int year, int month)
        {
            var products = await _db.Products
                .Where(p => p.IsActive)
                .Include(p => p.Family)
                .ToListAsync();

            var plans = await _db.MonthlyPlans
                .Where(mp => mp.Year == year && mp.Month == month)
                .ToDictionaryAsync(mp => mp.ProductId, mp => mp.PlannedQuantity);

            return products
                .OrderBy(p => p.Name)
                .Select(p => new MonthlyPlanProductDto(
                    p.Id, p.Name, p.FamilyId, p.Family?.Name,
                    plans.GetValueOrDefault(p.Id), IsComplete(p)))
                .ToList();
        }

        public async Task SetPlanAsync(int productId, int year, int month, int plannedQuantity)
        {
            if (plannedQuantity < 0)
                throw new ArgumentException("الكمية المخططة لازم تكون صفر أو أكتر", nameof(plannedQuantity));

            var existing = await _db.MonthlyPlans.FirstOrDefaultAsync(
                mp => mp.ProductId == productId && mp.Year == year && mp.Month == month);

            if (existing is null)
            {
                _db.MonthlyPlans.Add(new MonthlyPlan
                {
                    ProductId = productId, Year = year, Month = month, PlannedQuantity = plannedQuantity
                });
            }
            else
            {
                existing.PlannedQuantity = plannedQuantity;
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// "الخطة اليومية" — هدف يومي يدوي (MonthlyPlan.DailyTargetQuantity)،
        /// null بيشيله. مستقل عن الكمية المخططة الشهرية، فمنتج مالوش صف
        /// بعد لسه بيتعمله صف بـPlannedQuantity=0 عشان الرقم يتحفظ.
        /// </summary>
        public async Task SetDailyTargetAsync(int productId, int year, int month, int? dailyTarget)
        {
            if (dailyTarget is < 0)
                throw new ArgumentException("الخطة اليومية لازم تكون صفر أو أكتر", nameof(dailyTarget));

            var existing = await _db.MonthlyPlans.FirstOrDefaultAsync(
                mp => mp.ProductId == productId && mp.Year == year && mp.Month == month);

            if (existing is null)
            {
                _db.MonthlyPlans.Add(new MonthlyPlan
                {
                    ProductId = productId, Year = year, Month = month,
                    PlannedQuantity = 0, DailyTargetQuantity = dailyTarget
                });
            }
            else
            {
                existing.DailyTargetQuantity = dailyTarget;
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>الشهر ده فيه أي صف متسجل بالفعل؟ — الشاشة بتسأل تأكيد قبل النسخ لو آه</summary>
        public Task<bool> MonthHasEntriesAsync(int year, int month) =>
            _db.MonthlyPlans.AnyAsync(mp => mp.Year == year && mp.Month == month);

        /// <summary>
        /// ينسخ خطط الشهر اللي فات كنقطة بداية للشهر ده — Upsert: منتج
        /// عنده صف في الشهر ده بالفعل بيتحدّث بقيمة الشهر اللي فات
        /// (استبدال، بعد ما الشاشة تاخد تأكيد المستخدم لو فيه صفوف موجودة
        /// أصلًا — شوف MonthHasEntriesAsync)، ومنتج مالوش صف بيتضاف.
        /// </summary>
        public async Task<int> CopyFromPreviousMonthAsync(int year, int month)
        {
            var (prevYear, prevMonth) = month == 1 ? (year - 1, 12) : (year, month - 1);

            var previous = await _db.MonthlyPlans
                .Where(mp => mp.Year == prevYear && mp.Month == prevMonth)
                .ToListAsync();

            if (previous.Count == 0) return 0;

            var current = await _db.MonthlyPlans
                .Where(mp => mp.Year == year && mp.Month == month)
                .ToDictionaryAsync(mp => mp.ProductId);

            foreach (var prev in previous)
            {
                if (current.TryGetValue(prev.ProductId, out var existing))
                {
                    existing.PlannedQuantity = prev.PlannedQuantity;
                    existing.DailyTargetQuantity = prev.DailyTargetQuantity;
                }
                else
                    _db.MonthlyPlans.Add(new MonthlyPlan
                    {
                        ProductId = prev.ProductId, Year = year, Month = month,
                        PlannedQuantity = prev.PlannedQuantity, DailyTargetQuantity = prev.DailyTargetQuantity
                    });
            }

            await _db.SaveChangesAsync();
            return previous.Count;
        }
    }
}
