using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// خطة بفترة مخصصة (PlanPeriod) — نفس سيناريوهات MonthlyPlanServiceTests
    /// بالظبط، بس لفترة start/end بدل شهر تقويمي. شوف CLAUDE.md ليه الفيتشر
    /// ده موازي لـMonthlyPlan مش استبدال ليها.
    /// </summary>
    public class PlanPeriodServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        // سبتمبر 2026: فيه جمعتين (4، 11) — نفس شهر WorkCalendarRulesTests
        private static readonly DateTime Start = new(2026, 9, 1);
        private static readonly DateTime End = new(2026, 9, 15);

        // ═══════════ CreatePeriodAsync ═══════════

        [Fact]
        public async Task CreatePeriodAsync_computes_and_stores_workday_count()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(Start, End));

            Assert.Equal(Start, period.StartDate);
            Assert.Equal(End, period.EndDate);
            Assert.Equal(13, period.WorkdayCount); // 15 يوم - جمعتين (4، 11)
        }

        [Fact]
        public async Task CreatePeriodAsync_accounts_for_manual_holiday_inside_range()
        {
            using (var scope = _db.CreateScope())
            {
                _db.GetService<AppDbContext>(scope).MonthlyWorkCalendarHolidays.Add(
                    new MonthlyWorkCalendarHoliday { Date = new DateTime(2026, 9, 15) });
                await _db.GetService<AppDbContext>(scope).SaveChangesAsync();
            }

            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(Start, End));

            Assert.Equal(12, period.WorkdayCount); // 13 - يوم عطلة واحد جوه المدى
        }

        [Fact]
        public async Task CreatePeriodAsync_rejects_end_before_start()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(End, Start)));
        }

        [Fact]
        public async Task GetPeriodsAsync_orders_newest_start_date_first()
        {
            var older = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 10)));
            var newer = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(Start, End));

            var periods = await _db.InScopeAsync<PlanPeriodService, List<PlanPeriod>>(s => s.GetPeriodsAsync());

            Assert.Equal(newer.Id, periods[0].Id);
            Assert.Equal(older.Id, periods[1].Id);
        }

        // ═══════════ IsComplete (مشتركة مع MonthlyPlanService، مش نسخة تانية) ═══════════

        [Fact]
        public async Task GetForPeriodAsync_flags_incomplete_product_without_weight_or_material()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            var products = await _db.InScopeAsync<PlanPeriodService, List<PlanPeriodProductDto>>(
                s => s.GetForPeriodAsync(period.Id));

            var row = products.Single(p => p.ProductId == TestDatabase.ProductRingId);
            Assert.False(row.IsComplete);
            Assert.Null(row.FamilyId);
        }

        // ═══════════ SetTargetAsync (Upsert + فهرس فريد) ═══════════

        [Fact]
        public async Task SetTargetAsync_creates_then_updates_same_row_not_duplicate()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(period.Id, TestDatabase.ProductRingId, 100); return true; });
            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(period.Id, TestDatabase.ProductRingId, 250); return true; });

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<AppDbContext>(scope).PlanPeriodTargets
                .Where(t => t.PlanPeriodId == period.Id && t.ProductId == TestDatabase.ProductRingId)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(250, rows[0].PlannedQuantity);
        }

        [Fact]
        public async Task SetTargetAsync_rejects_negative_quantity()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _db.InScopeAsync<PlanPeriodService, bool>(async s =>
                { await s.SetTargetAsync(period.Id, TestDatabase.ProductRingId, -1); return true; }));
        }

        // ═══════════ نسخ خطة الفترة اللي فاتت ═══════════

        [Fact]
        public async Task CopyFromPreviousPeriodAsync_copies_from_the_latest_earlier_period()
        {
            var earlier = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 10)));
            var current = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(earlier.Id, TestDatabase.ProductRingId, 400); return true; });

            var copied = await _db.InScopeAsync<PlanPeriodService, int>(s => s.CopyFromPreviousPeriodAsync(current.Id));
            Assert.Equal(1, copied);

            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var currentRow = await db.PlanPeriodTargets.SingleAsync(t => t.PlanPeriodId == current.Id);
            var earlierRow = await db.PlanPeriodTargets.SingleAsync(t => t.PlanPeriodId == earlier.Id);

            Assert.Equal(400, currentRow.PlannedQuantity);
            Assert.Equal(400, earlierRow.PlannedQuantity); // الفترة السابقة مايتأثرش
        }

        [Fact]
        public async Task CopyFromPreviousPeriodAsync_upserts_existing_current_period_row()
        {
            var earlier = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 10)));
            var current = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(earlier.Id, TestDatabase.ProductRingId, 400); return true; });
            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(current.Id, TestDatabase.ProductRingId, 999); return true; });

            await _db.InScopeAsync<PlanPeriodService, int>(s => s.CopyFromPreviousPeriodAsync(current.Id));

            using var scope = _db.CreateScope();
            var rows = await _db.GetService<AppDbContext>(scope).PlanPeriodTargets
                .Where(t => t.PlanPeriodId == current.Id && t.ProductId == TestDatabase.ProductRingId)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal(400, rows[0].PlannedQuantity);
        }

        [Fact]
        public async Task CopyFromPreviousPeriodAsync_returns_zero_when_no_earlier_period_exists()
        {
            var current = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            var copied = await _db.InScopeAsync<PlanPeriodService, int>(s => s.CopyFromPreviousPeriodAsync(current.Id));
            Assert.Equal(0, copied);
        }

        [Fact]
        public async Task PeriodHasEntriesAsync_reflects_that_period_rows_only()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));
            var other = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(
                s => s.CreatePeriodAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 10)));

            Assert.False(await _db.InScopeAsync<PlanPeriodService, bool>(s => s.PeriodHasEntriesAsync(period.Id)));

            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(period.Id, TestDatabase.ProductRingId, 10); return true; });

            Assert.True(await _db.InScopeAsync<PlanPeriodService, bool>(s => s.PeriodHasEntriesAsync(period.Id)));
            Assert.False(await _db.InScopeAsync<PlanPeriodService, bool>(s => s.PeriodHasEntriesAsync(other.Id)));
        }

        // ═══════════ مجموع خطة العيلة — نفس منطق MonthlyPlanServiceTests بالظبط ═══════════

        [Fact]
        public async Task Family_subtotal_equals_exact_sum_of_its_products_targets()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("طقم اختبار فترة"));

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId = familyId;
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(period.Id, TestDatabase.ProductRingId, 300); return true; });
            await _db.InScopeAsync<PlanPeriodService, bool>(async s =>
            { await s.SetTargetAsync(period.Id, TestDatabase.ProductChainId, 150); return true; });

            var products = await _db.InScopeAsync<PlanPeriodService, List<PlanPeriodProductDto>>(
                s => s.GetForPeriodAsync(period.Id));

            var subtotal = products.Where(p => p.FamilyId == familyId).Sum(p => p.PlannedQuantity);
            Assert.Equal(450, subtotal);
        }

        [Fact]
        public async Task Unique_index_on_period_product_prevents_duplicate_rows_at_db_level()
        {
            var period = await _db.InScopeAsync<PlanPeriodService, PlanPeriod>(s => s.CreatePeriodAsync(Start, End));

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.PlanPeriodTargets.Add(new PlanPeriodTarget
                { PlanPeriodId = period.Id, ProductId = TestDatabase.ProductRingId, PlannedQuantity = 1 });
                await db.SaveChangesAsync();
            }

            using var scope2 = _db.CreateScope();
            var db2 = _db.GetService<AppDbContext>(scope2);
            db2.PlanPeriodTargets.Add(new PlanPeriodTarget
            { PlanPeriodId = period.Id, ProductId = TestDatabase.ProductRingId, PlannedQuantity = 2 });

            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        }
    }
}
