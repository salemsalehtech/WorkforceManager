using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    public class MonthlyPlanTrackingServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Today => TestDatabase.Today; // 2026-07-29 (أربع)
        private const int Year = 2026;
        private const int Month = 7;

        private async Task RecordChainProductionAsync(int pieces, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id, pieces, date, confirmOverride: true);
        }

        private async Task SetPlanAsync(int quantity)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, quantity);
        }

        private async Task SetClassificationAsync(int productId, decimal? weightGrams, Core.Enums.Material? material)
        {
            using var scope = _db.CreateScope();
            var product = await _db.GetService<AppDbContext>(scope).Products.FindAsync(productId);
            product!.PieceWeightGrams = weightGrams;
            product.Material = material;
            await _db.GetService<AppDbContext>(scope).SaveChangesAsync();
        }

        private Task<List<MonthlyPlanTrackingDto>> GetTrackingAsync(DateTime asOfDate) =>
            _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanTrackingDto>>(
                s => s.GetTrackingAsync(Year, Month, asOfDate));

        // ═══════════ المحقق من بيانات الإنتاج الحقيقية (مش رقم مكتوب) ═══════════

        [Fact]
        public async Task AchievedToDate_reflects_real_recorded_production_on_the_last_stage()
        {
            await RecordChainProductionAsync(120, Today);
            await SetPlanAsync(400);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(120, row.AchievedToDate);
            Assert.Equal(0, row.CorrectionsToDate);
            Assert.Equal(120, row.EffectiveAchieved);
        }

        [Fact]
        public async Task ProRatedPlan_uses_elapsed_over_total_workdays_not_flat_month_plan()
        {
            await SetPlanAsync(2600); // رقم سهل القسمة (26 يوم شغل متوقع في يوليو 2026)

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            var holidays = new HashSet<DateTime>();
            var total = WorkCalendarRules.TotalWorkdays(Year, Month, holidays);
            var elapsed = WorkCalendarRules.ElapsedWorkdays(Year, Month, Today, holidays);
            var expectedProRated = 2600m * elapsed / total;

            Assert.Equal(expectedProRated, row.ProRatedPlan);
            // المتوقّع لحد النهارده (ProRatedPlan) أصغر بكتير من خطة الشهر الكاملة —
            // ده جوهر الـpro-ration، مش مقارنة محقق بخطة الشهر ككل
            Assert.True(row.ProRatedPlan < 2600m);
        }

        [Fact]
        public async Task AchievedPercent_is_null_when_prorated_plan_is_zero()
        {
            // مفيش خطة خالص للمنتج ده الشهر ده
            var row = (await GetTrackingAsync(Today)).SingleOrDefault(r => r.ProductId == TestDatabase.ProductChainId);
            if (row is null) return; // مفيش نشاط ولا خطة، مش متوقع يظهر أصلاً

            Assert.Null(row.AchievedPercent);
        }

        // ═══════════ إجمالي الوزن = وزن القطعة × المحقق الفعلي ═══════════

        [Fact]
        public async Task TotalWeightGrams_equals_piece_weight_times_effective_achieved()
        {
            await RecordChainProductionAsync(100, Today);
            await SetClassificationAsync(TestDatabase.ProductChainId, weightGrams: 12.5m, Core.Enums.Material.Copper);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(1250m, row.TotalWeightGrams); // 12.5 × 100
            Assert.Equal(Core.Enums.Material.Copper, row.Material);
        }

        [Fact]
        public async Task TotalWeightGrams_is_null_when_product_has_no_weight()
        {
            await RecordChainProductionAsync(100, Today);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Null(row.TotalWeightGrams);
        }

        [Fact]
        public async Task TotalWeightGrams_includes_manual_corrections()
        {
            await RecordChainProductionAsync(100, Today);
            await SetClassificationAsync(TestDatabase.ProductChainId, weightGrams: 10m, Core.Enums.Material.Zamak);
            await _db.InScopeAsync<MonthlyPlanTrackingService, bool>(async s =>
            { await s.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 20); return true; });

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(1200m, row.TotalWeightGrams); // 10 × (100 + 20)
        }

        // ═══════════ تصليحات — يدوي بالكامل، مقصور على يوم واحد ═══════════

        [Fact]
        public async Task Correction_is_added_on_top_of_real_achieved_never_replacing_it()
        {
            await RecordChainProductionAsync(100, Today);
            using (var scope = _db.CreateScope())
                await _db.GetService<MonthlyPlanTrackingService>(scope).SetCorrectionAsync(TestDatabase.ProductChainId, Today, 30);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(100, row.AchievedToDate);
            Assert.Equal(30, row.CorrectionsToDate);
            Assert.Equal(130, row.EffectiveAchieved);
        }

        [Fact]
        public async Task Correction_never_silently_overwrites_a_different_day()
        {
            var yesterday = Today.AddDays(-1);
            using (var scope = _db.CreateScope())
            {
                var svc = _db.GetService<MonthlyPlanTrackingService>(scope);
                await svc.SetCorrectionAsync(TestDatabase.ProductChainId, yesterday, 20);
                await svc.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 50);
            }

            using var readScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(readScope);
            var yesterdayRow = db.MonthlyPlanCorrections.Single(c => c.Date == yesterday.Date);
            var todayRow = db.MonthlyPlanCorrections.Single(c => c.Date == Today.Date);

            Assert.Equal(20, yesterdayRow.Quantity);
            Assert.Equal(50, todayRow.Quantity);
        }

        [Fact]
        public async Task Correction_upsert_updates_same_day_row_instead_of_duplicating()
        {
            using (var scope = _db.CreateScope())
            {
                var svc = _db.GetService<MonthlyPlanTrackingService>(scope);
                await svc.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 10);
                await svc.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 999);
            }

            using var readScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(readScope);
            var rows = db.MonthlyPlanCorrections.Where(c => c.ProductId == TestDatabase.ProductChainId && c.Date == Today.Date).ToList();

            Assert.Single(rows);
            Assert.Equal(999, rows[0].Quantity);
        }

        // ═══════════ المطلوب يوميًا — بيتغيّر مع التصليحات وتقدّم الشهر ═══════════

        [Fact]
        public async Task RequiredDailyOutput_decreases_after_a_positive_correction()
        {
            await SetPlanAsync(1000);
            await RecordChainProductionAsync(100, Today);

            var before = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId).RequiredDailyOutput;

            using (var scope = _db.CreateScope())
                await _db.GetService<MonthlyPlanTrackingService>(scope).SetCorrectionAsync(TestDatabase.ProductChainId, Today, 200);

            var after = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId).RequiredDailyOutput;

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.True(after < before);
        }

        [Fact]
        public async Task RequiredDailyOutput_is_null_when_no_workdays_remain()
        {
            await SetPlanAsync(1000);
            var lastWorkday = new DateTime(Year, Month, 30); // آخر يوم شغل فعلي في يوليو 2026 (31 جمعة)

            var row = (await GetTrackingAsync(lastWorkday)).SingleOrDefault(r => r.ProductId == TestDatabase.ProductChainId);
            Assert.NotNull(row);
            Assert.Null(row!.RequiredDailyOutput);
        }

        // ═══════════ إنتاج خارج الخطة ═══════════

        [Fact]
        public async Task Product_with_production_but_no_plan_is_flagged_outside_plan_not_silently_zero_percent()
        {
            await RecordChainProductionAsync(50, Today); // مفيش SetPlanAsync خالص

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.True(row.IsOutsidePlan);
            Assert.Equal(0, row.PlannedQuantity);
            Assert.Equal(50, row.EffectiveAchieved);
        }

        // ═══════════ تقويم أيام الشغل — الفريضة والعطلة اليدوية ═══════════

        [Fact]
        public async Task Manual_holiday_reduces_remaining_workdays_and_raises_required_daily_output()
        {
            // Today = 29 يوليو (أربع) — يوم 30 (خميس) هو يوم الشغل الوحيد
            // الباقي بعده في الشهر (31 جمعة أصلاً). تحويل 30 لعطلة يسيب صفر
            await SetPlanAsync(1000);

            var beforeRow = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);
            Assert.NotNull(beforeRow.RequiredDailyOutput); // لسه يوم 30 باقي

            using (var scope = _db.CreateScope())
                await _db.GetService<MonthlyPlanTrackingService>(scope).AddHolidayAsync(new DateTime(Year, Month, 30));

            var afterRow = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);
            Assert.Null(afterRow.RequiredDailyOutput); // مفيش يوم شغل متبقي خالص دلوقتي
        }

        [Fact]
        public async Task AddHolidayAsync_rejects_duplicate_date()
        {
            using var scope = _db.CreateScope();
            var svc = _db.GetService<MonthlyPlanTrackingService>(scope);
            await svc.AddHolidayAsync(new DateTime(Year, Month, 15));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.AddHolidayAsync(new DateTime(Year, Month, 15)));
        }
    }
}
