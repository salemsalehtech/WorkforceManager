using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    public class MonthlyPlanServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private const int Year = 2026;
        private const int Month = 7;

        private async Task SetClassificationAsync(int productId, int? familyId, decimal? weight, Material? material)
        {
            using var scope = _db.CreateScope();
            var product = await _db.GetService<AppDbContext>(scope).Products.FindAsync(productId);
            product!.FamilyId = familyId;
            product.PieceWeightGrams = weight;
            product.Material = material;
            await _db.GetService<AppDbContext>(scope).SaveChangesAsync();
        }

        // ═══════════ "مكتمل البيانات" — وزن + مادة، العيلة مش شرط ═══════════

        [Fact]
        public void IsComplete_requires_weight_and_material_but_not_family()
        {
            var withBoth = new Product { PieceWeightGrams = 5.5m, Material = Material.Copper };
            var noFamilyStillComplete = new Product { PieceWeightGrams = 5.5m, Material = Material.Copper, FamilyId = null };
            var missingWeight = new Product { PieceWeightGrams = null, Material = Material.Copper };
            var missingMaterial = new Product { PieceWeightGrams = 5.5m, Material = null };
            var missingBoth = new Product();

            Assert.True(MonthlyPlanService.IsComplete(withBoth));
            Assert.True(MonthlyPlanService.IsComplete(noFamilyStillComplete));
            Assert.False(MonthlyPlanService.IsComplete(missingWeight));
            Assert.False(MonthlyPlanService.IsComplete(missingMaterial));
            Assert.False(MonthlyPlanService.IsComplete(missingBoth));
        }

        [Fact]
        public async Task GetForMonthAsync_flags_incomplete_product_without_weight_or_material()
        {
            // منتج من غير عيلة، من غير وزن ولا مادة — لازم يظهر وناقص البيانات
            var products = await _db.InScopeAsync<MonthlyPlanService, System.Collections.Generic.List<MonthlyPlanProductDto>>(
                s => s.GetForMonthAsync(Year, Month));

            var row = products.Single(p => p.ProductId == TestDatabase.ProductRingId);
            Assert.False(row.IsComplete);
            Assert.Null(row.FamilyId);
        }

        [Fact]
        public async Task GetForMonthAsync_marks_complete_once_weight_and_material_set_without_family()
        {
            await SetClassificationAsync(TestDatabase.ProductRingId, familyId: null, weight: 3.2m, material: Material.Zamak);

            var products = await _db.InScopeAsync<MonthlyPlanService, System.Collections.Generic.List<MonthlyPlanProductDto>>(
                s => s.GetForMonthAsync(Year, Month));

            var row = products.Single(p => p.ProductId == TestDatabase.ProductRingId);
            Assert.True(row.IsComplete);
            Assert.Null(row.FamilyId); // بدون عيلة، بس لسه "مكتمل"
        }

        // ═══════════ SetPlanAsync (Upsert + فهرس فريد) ═══════════

        [Fact]
        public async Task SetPlanAsync_creates_then_updates_same_row_not_duplicate()
        {
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 100); return true; });

            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 250); return true; });

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .Where(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(250, rows[0].PlannedQuantity);
        }

        [Fact]
        public async Task SetPlanAsync_rejects_negative_quantity()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
                { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, -1); return true; }));
        }

        // ═══════════ الخطة اليومية (هدف يدوي، مستقل عن الكمية الشهرية) ═══════════

        [Fact]
        public async Task SetDailyTargetAsync_creates_row_with_zero_plan_when_none_exists()
        {
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, Month, 500); return true; });

            using var scope = _db.CreateScope();
            var row = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .SingleAsync(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month);

            Assert.Equal(500, row.DailyTargetQuantity);
            Assert.Equal(0, row.PlannedQuantity); // مفيش خطة شهرية اتحطت، بس الصف لازم يتعمل عشان الهدف يتحفظ
        }

        [Fact]
        public async Task SetDailyTargetAsync_does_not_touch_existing_planned_quantity()
        {
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 9000); return true; });
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, Month, 400); return true; });

            using var scope = _db.CreateScope();
            var row = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .SingleAsync(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month);

            Assert.Equal(9000, row.PlannedQuantity); // زي ما كانت
            Assert.Equal(400, row.DailyTargetQuantity);
        }

        [Fact]
        public async Task SetDailyTargetAsync_null_clears_the_target_not_sets_zero()
        {
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, Month, 500); return true; });
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, Month, null); return true; });

            using var scope = _db.CreateScope();
            var row = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .SingleAsync(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month);

            Assert.Null(row.DailyTargetQuantity);
        }

        [Fact]
        public async Task SetDailyTargetAsync_rejects_negative_value()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
                { await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, Month, -1); return true; }));
        }

        [Fact]
        public async Task CopyFromPreviousMonthAsync_carries_daily_target_too()
        {
            var prevMonth = Month - 1;
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            {
                await s.SetPlanAsync(TestDatabase.ProductRingId, Year, prevMonth, 6000);
                await s.SetDailyTargetAsync(TestDatabase.ProductRingId, Year, prevMonth, 300);
                return true;
            });

            await _db.InScopeAsync<MonthlyPlanService, int>(s => s.CopyFromPreviousMonthAsync(Year, Month));

            using var scope = _db.CreateScope();
            var row = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .SingleAsync(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month);

            Assert.Equal(300, row.DailyTargetQuantity);
        }

        // ═══════════ نسخ خطة الشهر اللي فات ═══════════

        [Fact]
        public async Task CopyFromPreviousMonthAsync_creates_rows_only_in_current_month()
        {
            var prevMonth = Month - 1;
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, prevMonth, 400); return true; });

            var copied = await _db.InScopeAsync<MonthlyPlanService, int>(s => s.CopyFromPreviousMonthAsync(Year, Month));
            Assert.Equal(1, copied);

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var currentRow = await db.MonthlyPlans.SingleAsync(mp => mp.Year == Year && mp.Month == Month);
            var previousRow = await db.MonthlyPlans.SingleAsync(mp => mp.Year == Year && mp.Month == prevMonth);

            Assert.Equal(400, currentRow.PlannedQuantity);
            Assert.Equal(400, previousRow.PlannedQuantity); // الشهر اللي فات مايتأثرش
        }

        [Fact]
        public async Task CopyFromPreviousMonthAsync_upserts_existing_current_month_row()
        {
            var prevMonth = Month - 1;
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, prevMonth, 400); return true; });

            // الشهر الحالي عنده قيمة قديمة بالفعل — النسخ لازم يستبدلها من غير ما يضاعف الصف
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 999); return true; });

            await _db.InScopeAsync<MonthlyPlanService, int>(s => s.CopyFromPreviousMonthAsync(Year, Month));

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<AppDbContext>(scope).MonthlyPlans
                .Where(mp => mp.ProductId == TestDatabase.ProductRingId && mp.Year == Year && mp.Month == Month)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(400, rows[0].PlannedQuantity);
        }

        [Fact]
        public async Task MonthHasEntriesAsync_reflects_current_month_rows_only()
        {
            Assert.False(await _db.InScopeAsync<MonthlyPlanService, bool>(s => s.MonthHasEntriesAsync(Year, Month)));

            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 10); return true; });

            Assert.True(await _db.InScopeAsync<MonthlyPlanService, bool>(s => s.MonthHasEntriesAsync(Year, Month)));
            Assert.False(await _db.InScopeAsync<MonthlyPlanService, bool>(s => s.MonthHasEntriesAsync(Year, Month - 1)));
        }

        // ═══════════ مجموع خطة العيلة (Subtotal) — الـViewModel بيعمل Sum
        // بسيط على PlannedQuantity من نتيجة GetForMonthAsync، فالاختبار
        // هنا بيتحقق من نفس الحسبة على البيانات اللي الخدمة بترجّعها ═══════════

        [Fact]
        public async Task Family_subtotal_equals_exact_sum_of_its_products_plans()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("طقم اختبار"));

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId = familyId;
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 300); return true; });
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 150); return true; });

            var products = await _db.InScopeAsync<MonthlyPlanService, System.Collections.Generic.List<MonthlyPlanProductDto>>(
                s => s.GetForMonthAsync(Year, Month));

            var subtotal = products.Where(p => p.FamilyId == familyId).Sum(p => p.PlannedQuantity);
            Assert.Equal(450, subtotal);
        }

        [Fact]
        public async Task Family_subtotal_is_zero_when_family_has_no_products()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عيلة من غير منتجات"));

            var products = await _db.InScopeAsync<MonthlyPlanService, System.Collections.Generic.List<MonthlyPlanProductDto>>(
                s => s.GetForMonthAsync(Year, Month));

            var subtotal = products.Where(p => p.FamilyId == familyId).Sum(p => p.PlannedQuantity);
            Assert.Equal(0, subtotal);
        }

        [Fact]
        public async Task Family_subtotal_drops_product_removed_from_family_mid_month()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("طقم متغيّر"));

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId = familyId;
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 200); return true; });
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 100); return true; });

            // منتج بيتشال من العيلة في نص الشهر — خطته تفضل موجودة، بس مابقاش جزء من مجموعها
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = null;
                await db.SaveChangesAsync();
            }

            var products = await _db.InScopeAsync<MonthlyPlanService, System.Collections.Generic.List<MonthlyPlanProductDto>>(
                s => s.GetForMonthAsync(Year, Month));

            var subtotal = products.Where(p => p.FamilyId == familyId).Sum(p => p.PlannedQuantity);
            Assert.Equal(200, subtotal); // بس المنتج اللي لسه في العيلة

            var chainRow = products.Single(p => p.ProductId == TestDatabase.ProductChainId);
            Assert.Null(chainRow.FamilyId); // خطته لسه موجودة، بس بدون عيلة دلوقتي
            Assert.Equal(100, chainRow.PlannedQuantity);
        }

        [Fact]
        public async Task Unique_index_on_product_year_month_prevents_duplicate_rows_at_db_level()
        {
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.MonthlyPlans.Add(new MonthlyPlan
                { ProductId = TestDatabase.ProductRingId, Year = Year, Month = Month, PlannedQuantity = 1 });
                await db.SaveChangesAsync();
            }

            using var scope2 = _db.CreateScope();
            var db2 = _db.GetService<AppDbContext>(scope2);
            db2.MonthlyPlans.Add(new MonthlyPlan
            { ProductId = TestDatabase.ProductRingId, Year = Year, Month = Month, PlannedQuantity = 2 });

            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        }
    }
}
