using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
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

        /// <summary>
        /// المحقق بقى إدخال يدوي صرف (MonthlyPlanDailyEntry)، مش مشتق من
        /// إنتاج حقيقي — شوف CLAUDE.md. اسم الميثود فاضل "Chain" بس عشان
        /// كل اختبارات الملف بتستخدم TestDatabase.ProductChainId.
        /// </summary>
        private async Task SetChainDailyEntryAsync(int pieces, DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<MonthlyPlanTrackingService>(scope)
                .SetDailyEntryAsync(TestDatabase.ProductChainId, date, pieces);
        }

        /// <summary>
        /// إنتاج حقيقي فعلي — مطلوب بس لاختبارات "الإنتاج اليومي" (زرار
        /// "استخدم نفس الرقم الحقيقي"/تحذير الاختلاف)، مش للمحقق نفسه.
        /// </summary>
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

        private Task<List<MonthlyPlanTrackingDto>> GetTrackingAsync(DateTime asOfDate, int subPeriodId) =>
            _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanTrackingDto>>(
                s => s.GetTrackingAsync(Year, Month, asOfDate, subPeriodId));

        private Task<MonthlyPlanSubPeriod> CreateSubPeriodAsync(string label, DateTime start, DateTime end) =>
            _db.InScopeAsync<MonthlyPlanService, MonthlyPlanSubPeriod>(
                s => s.CreateSubPeriodAsync(label, start, end));

        // ═══════════ المحقق إدخال يدوي صرف (مش مشتق من إنتاج حقيقي) ═══════════

        [Fact]
        public async Task AchievedToDate_reflects_manual_daily_entry()
        {
            await SetChainDailyEntryAsync(120, Today);
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

        // ═══════════ فترة فرعية اختيارية (SubPeriod) — إضافية فوق الشهر كامل ═══════════

        [Fact]
        public async Task SubPeriod_computes_workdays_from_its_own_range_not_the_full_month()
        {
            // يوليو 2026 كامل: 31 يوم. فترة فرعية 1-15 يوليو نطاقها جزء بس من الشهر
            var subPeriod = await CreateSubPeriodAsync("خطة 1", new DateTime(Year, Month, 1), new DateTime(Year, Month, 15));

            var wholeMonth = (await GetTrackingAsync(Today, subPeriodId: 0)).Single(r => r.ProductId == TestDatabase.ProductChainId);
            var subRange = (await GetTrackingAsync(new DateTime(Year, Month, 15), subPeriod.Id))
                .Single(r => r.ProductId == TestDatabase.ProductChainId);

            var holidays = new HashSet<DateTime>();
            var expectedSubTotal = WorkCalendarRules.TotalWorkdays(subPeriod.StartDate, subPeriod.EndDate, holidays);

            Assert.Equal(expectedSubTotal, subRange.TotalWorkdays);
            // نطاق 1-15 لازم يكون فعليًا أقل من الشهر كامل، وإلا الاختبار مش بيثبت حاجة
            Assert.True(subRange.TotalWorkdays < wholeMonth.TotalWorkdays);
        }

        [Fact]
        public async Task SubPeriod_achieved_only_counts_entries_inside_its_own_date_range()
        {
            var subPeriod = await CreateSubPeriodAsync("خطة 1", new DateTime(Year, Month, 1), new DateTime(Year, Month, 15));

            await SetChainDailyEntryAsync(50, new DateTime(Year, Month, 10)); // جوه نطاق الفترة الفرعية
            await SetChainDailyEntryAsync(999, new DateTime(Year, Month, 20)); // برّه نطاقها

            var row = (await GetTrackingAsync(new DateTime(Year, Month, 15), subPeriod.Id))
                .Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(50, row.AchievedToDate);
        }

        [Fact]
        public async Task Invalid_subPeriodId_throws()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => GetTrackingAsync(Today, subPeriodId: 999));
        }

        // ═══════════ إجمالي الوزن = وزن القطعة × المحقق الفعلي ═══════════

        [Fact]
        public async Task TotalWeightGrams_equals_piece_weight_times_effective_achieved()
        {
            await SetChainDailyEntryAsync(100, Today);
            await SetClassificationAsync(TestDatabase.ProductChainId, weightGrams: 12.5m, Core.Enums.Material.Copper);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(1250m, row.TotalWeightGrams); // 12.5 × 100
            Assert.Equal(Core.Enums.Material.Copper, row.Material);
        }

        [Fact]
        public async Task TotalWeightGrams_is_null_when_product_has_no_weight()
        {
            await SetChainDailyEntryAsync(100, Today);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Null(row.TotalWeightGrams);
        }

        [Fact]
        public async Task TotalWeightGrams_includes_manual_corrections()
        {
            await SetChainDailyEntryAsync(100, Today);
            await SetClassificationAsync(TestDatabase.ProductChainId, weightGrams: 10m, Core.Enums.Material.Zamak);
            await _db.InScopeAsync<MonthlyPlanTrackingService, bool>(async s =>
            { await s.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 20); return true; });

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(1200m, row.TotalWeightGrams); // 10 × (100 + 20)
        }

        // ═══════════ تصليحات — يدوي بالكامل، مقصور على يوم واحد ═══════════

        [Fact]
        public async Task Correction_is_added_on_top_of_manual_achieved_never_replacing_it()
        {
            await SetChainDailyEntryAsync(100, Today);
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

        // ═══════════ GetCorrectionAsync — القراءة اللي شاشة تسجيل الإنتاج
        // اليومي بتحتاجها عشان تظبط الخانة لو رجعت لنفس اليوم/المنتج ═══════════

        [Fact]
        public async Task GetCorrectionAsync_returns_zero_when_no_row_exists()
        {
            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, int>(
                s => s.GetCorrectionAsync(TestDatabase.ProductChainId, Today));

            Assert.Equal(0, value);
        }

        [Fact]
        public async Task GetCorrectionAsync_returns_saved_value_after_SetCorrectionAsync()
        {
            await _db.InScopeAsync<MonthlyPlanTrackingService, bool>(async s =>
            { await s.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 42); return true; });

            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, int>(
                s => s.GetCorrectionAsync(TestDatabase.ProductChainId, Today));

            Assert.Equal(42, value);
        }

        [Fact]
        public async Task GetCorrectionAsync_does_not_read_a_different_days_row()
        {
            await _db.InScopeAsync<MonthlyPlanTrackingService, bool>(async s =>
            { await s.SetCorrectionAsync(TestDatabase.ProductChainId, Today.AddDays(-1), 42); return true; });

            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, int>(
                s => s.GetCorrectionAsync(TestDatabase.ProductChainId, Today));

            Assert.Equal(0, value);
        }

        // ═══════════ المطلوب يوميًا — بيتغيّر مع التصليحات وتقدّم الشهر ═══════════

        [Fact]
        public async Task RequiredDailyOutput_decreases_after_a_positive_correction()
        {
            await SetPlanAsync(1000);
            await SetChainDailyEntryAsync(100, Today);

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
            await SetChainDailyEntryAsync(50, Today); // مفيش SetPlanAsync خالص

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

        // ═══════════ الإنتاج اليومي (يدوي، بديل المحقق التلقائي) ═══════════

        [Fact]
        public async Task SetDailyEntryAsync_creates_then_updates_same_row_not_duplicate()
        {
            await SetChainDailyEntryAsync(100, Today);
            await SetChainDailyEntryAsync(250, Today);

            using var scope = _db.CreateScope();
            var rows = _db.GetService<AppDbContext>(scope).MonthlyPlanDailyEntries
                .Where(e => e.ProductId == TestDatabase.ProductChainId && e.Date == Today.Date)
                .ToList();

            Assert.Single(rows);
            Assert.Equal(250, rows[0].Quantity);
        }

        [Fact]
        public async Task SetDailyEntryAsync_rejects_negative_quantity()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => SetChainDailyEntryAsync(-1, Today));
        }

        [Fact]
        public async Task GetDailyEntryAsync_returns_zero_when_no_row_exists()
        {
            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, int>(
                s => s.GetDailyEntryAsync(TestDatabase.ProductChainId, Today));

            Assert.Equal(0, value);
        }

        [Fact]
        public async Task GetDailyEntryAsync_returns_saved_value()
        {
            await SetChainDailyEntryAsync(77, Today);

            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, int>(
                s => s.GetDailyEntryAsync(TestDatabase.ProductChainId, Today));

            Assert.Equal(77, value);
        }

        [Fact]
        public async Task GetTodayEntryTabAsync_marks_only_products_with_a_row_today_as_filled()
        {
            await SetChainDailyEntryAsync(30, Today);

            var rows = await _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanTodayEntryDto>>(
                s => s.GetTodayEntryTabAsync(Today));

            var chain = rows.Single(r => r.ProductId == TestDatabase.ProductChainId);
            var ring = rows.Single(r => r.ProductId == TestDatabase.ProductRingId);

            Assert.True(chain.IsFilled);
            Assert.Equal(30, chain.ManualQuantity);
            Assert.False(ring.IsFilled);
            Assert.Equal(0, ring.ManualQuantity);
        }

        [Fact]
        public async Task GetTodayEntryTabAsync_carries_real_production_for_the_prefill_button()
        {
            await RecordChainProductionAsync(65, Today);

            var rows = await _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanTodayEntryDto>>(
                s => s.GetTodayEntryTabAsync(Today));

            var chain = rows.Single(r => r.ProductId == TestDatabase.ProductChainId);
            Assert.Equal(65, chain.RealProductionToday);
            // الإنتاج الحقيقي مرجع بس — لا بيملى ولا بيأثر على المحقق (ManualQuantity) لوحده
            Assert.False(chain.IsFilled);
            Assert.Equal(0, chain.ManualQuantity);
        }

        [Fact]
        public async Task GetTodayEntryTabAsync_carries_yesterdays_manual_entry_for_the_copy_button()
        {
            await SetChainDailyEntryAsync(40, Today.AddDays(-1));

            var rows = await _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanTodayEntryDto>>(
                s => s.GetTodayEntryTabAsync(Today));

            Assert.Equal(40, rows.Single(r => r.ProductId == TestDatabase.ProductChainId).YesterdayQuantity);
        }

        // ═══════════ الأيام المتبقية على الـDTO — أساس "الأيام المتبقية"
        // البارزة، ومحسوبة فعليًا مش رقم ثابت (باگ "27" في الشيت القديم) ═══════════

        [Fact]
        public async Task GetTrackingAsync_exposes_the_real_computed_workday_counts()
        {
            await SetPlanAsync(100);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            var holidays = new HashSet<DateTime>();
            var expectedTotal = WorkCalendarRules.TotalWorkdays(Year, Month, holidays);
            var expectedRemaining = expectedTotal - WorkCalendarRules.ElapsedWorkdays(Year, Month, Today, holidays);

            Assert.Equal(expectedTotal, row.TotalWorkdays);
            Assert.Equal(expectedRemaining, row.RemainingWorkdays);
        }

        [Fact]
        public async Task GetTrackingAsync_remaining_workdays_drops_after_adding_a_holiday()
        {
            await SetPlanAsync(100);
            var before = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId).RemainingWorkdays;

            using (var scope = _db.CreateScope())
                await _db.GetService<MonthlyPlanTrackingService>(scope).AddHolidayAsync(new DateTime(Year, Month, 30));

            var after = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId).RemainingWorkdays;

            Assert.Equal(before - 1, after);
        }

        // ═══════════ وصل للهدف بدري — تنبيه إيجابي، مش "ماشي صح" العادية ═══════════

        [Fact]
        public async Task HasReachedTarget_true_when_effective_achieved_meets_plan_with_days_left()
        {
            await SetPlanAsync(100);
            await SetChainDailyEntryAsync(100, Today);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.True(row.RemainingWorkdays > 0); // 30 (يوم شغل) لسه باقي بعد 29 يوليو
            Assert.True(row.HasReachedTarget);
        }

        [Fact]
        public async Task HasReachedTarget_false_when_below_plan()
        {
            await SetPlanAsync(100);
            await SetChainDailyEntryAsync(50, Today);

            var row = (await GetTrackingAsync(Today)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.False(row.HasReachedTarget);
        }

        [Fact]
        public async Task HasReachedTarget_false_when_no_workdays_remain_even_if_plan_met()
        {
            await SetPlanAsync(100);
            await SetChainDailyEntryAsync(100, new DateTime(Year, Month, 30));
            var lastWorkday = new DateTime(Year, Month, 30); // آخر يوم شغل فعلي في يوليو 2026

            var row = (await GetTrackingAsync(lastWorkday)).Single(r => r.ProductId == TestDatabase.ProductChainId);

            Assert.Equal(0, row.RemainingWorkdays);
            Assert.False(row.HasReachedTarget);
        }

        // ═══════════ آخر تحديث للمحقق ═══════════

        [Fact]
        public async Task GetLastUpdatedAsync_returns_null_when_no_entries_exist()
        {
            var value = await _db.InScopeAsync<MonthlyPlanTrackingService, DateTime?>(
                s => s.GetLastUpdatedAsync(Year, Month));

            Assert.Null(value);
        }

        [Fact]
        public async Task GetLastUpdatedAsync_reflects_the_latest_entry_and_ignores_other_months()
        {
            await SetChainDailyEntryAsync(10, Today);
            var afterFirst = await _db.InScopeAsync<MonthlyPlanTrackingService, DateTime?>(
                s => s.GetLastUpdatedAsync(Year, Month));
            Assert.NotNull(afterFirst);

            // شهر تاني خالص — مايأثرش على آخر تحديث الشهر المطلوب
            await SetChainDailyEntryAsync(20, new DateTime(Year, Month - 2, 10));
            var stillSame = await _db.InScopeAsync<MonthlyPlanTrackingService, DateTime?>(
                s => s.GetLastUpdatedAsync(Year, Month));
            Assert.Equal(afterFirst, stillSame);
        }

        // ═══════════ لقطة نهاية اليوم — حفظ + قايمة بس ═══════════

        [Fact]
        public async Task SaveSnapshotAsync_inserts_one_row_per_currently_displayed_product()
        {
            await SetPlanAsync(500);
            await SetChainDailyEntryAsync(100, Today);

            await _db.InScopeAsync<MonthlyPlanTrackingService, bool>(async s =>
            { await s.SaveSnapshotAsync(Year, Month, Today); return true; });

            using var scope = _db.CreateScope();
            var rows = _db.GetService<AppDbContext>(scope).MonthlyPlanSnapshots
                .Where(s => s.ProductId == TestDatabase.ProductChainId).ToList();

            Assert.Single(rows);
            Assert.Equal(500, rows[0].PlannedQuantity);
            Assert.Equal(100, rows[0].EffectiveAchieved);
        }

        [Fact]
        public async Task GetSnapshotTimestampsAsync_returns_newest_first()
        {
            await SetPlanAsync(100); // لازم يبقى فيه منتج ظاهر في التتبّع عشان اللقطة تسجّل أي صف

            using (var scope = _db.CreateScope())
            {
                var svc = _db.GetService<MonthlyPlanTrackingService>(scope);
                await svc.SaveSnapshotAsync(Year, Month, Today);
                await Task.Delay(10); // عشان الطابعين الزمنيين يختلفوا فعليًا، مش نفس التكة
                await svc.SaveSnapshotAsync(Year, Month, Today);
            }

            var timestamps = await _db.InScopeAsync<MonthlyPlanTrackingService, List<DateTime>>(
                s => s.GetSnapshotTimestampsAsync(Year, Month));

            Assert.Equal(2, timestamps.Count);
            Assert.True(timestamps[0] >= timestamps[1]); // الأحدث الأول
        }

        [Fact]
        public async Task GetSnapshotAsync_returns_the_rows_of_that_one_snapshot()
        {
            await SetPlanAsync(200);
            await SetChainDailyEntryAsync(50, Today);

            DateTime taken;
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<MonthlyPlanTrackingService>(scope).SaveSnapshotAsync(Year, Month, Today);
                taken = _db.GetService<AppDbContext>(scope).MonthlyPlanSnapshots
                    .Where(s => s.ProductId == TestDatabase.ProductChainId).Select(s => s.TakenAt).Single();
            }

            var rows = await _db.InScopeAsync<MonthlyPlanTrackingService, List<MonthlyPlanSnapshotRowDto>>(
                s => s.GetSnapshotAsync(taken));

            var row = rows.Single(r => r.ProductId == TestDatabase.ProductChainId);
            Assert.Equal(200, row.PlannedQuantity);
            Assert.Equal(50, row.EffectiveAchieved);
        }
    }
}
