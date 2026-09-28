using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using WorkforceManager.Business.Services;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    public class MonthlyPlanExcelServiceTests : IDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"wfm-plan-export-{Guid.NewGuid():N}.xlsx");

        public void Dispose()
        {
            _db.Dispose();
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }

        private static DateTime Today => TestDatabase.Today;
        private const int Year = 2026;
        private const int Month = 7;

        // عمود 1 = المنتج/التسمية، عمود 2 = مخطط، عمود 3 = محقق، عمود 5 = Ach %، عمود 6 = نسبة المحقق، عمود 9 = المطلوب يوميًا، عمود 11 = الوزن
        private const int ColLabel = 1, ColPlan = 2, ColAchieved = 3, ColAchPercent = 5, ColProRated = 6, ColRequiredDaily = 9, ColWeight = 11;

        private Task<List<Business.DTOs.MonthlyPlanTrackingDto>> GetTrackingAsync() =>
            _db.InScopeAsync<MonthlyPlanTrackingService, List<Business.DTOs.MonthlyPlanTrackingDto>>(
                s => s.GetTrackingAsync(Year, Month, Today));

        private static int FindRowByLabel(IXLWorksheet sheet, string label)
        {
            var lastRow = sheet.LastRowUsed()!.RowNumber();
            for (var r = 1; r <= lastRow; r++)
                if (sheet.Cell(r, ColLabel).GetString() == label) return r;
            throw new InvalidOperationException($"صف '{label}' مش موجود في الملف");
        }

        [Fact]
        public async Task Export_totals_match_the_same_tracking_data_the_screen_reads()
        {
            using (var scope = _db.CreateScope())
            {
                var trackingService = _db.GetService<MonthlyPlanTrackingService>(scope);
                await trackingService.SetDailyEntryAsync(TestDatabase.ProductChainId, Today, 150);
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 500);
                await trackingService.SetCorrectionAsync(TestDatabase.ProductChainId, Today, 20);
            }

            var tracking = await GetTrackingAsync();
            var expectedPlan = tracking.Where(t => !t.IsOutsidePlan).Sum(t => t.PlannedQuantity);
            var expectedAchieved = tracking.Where(t => !t.IsOutsidePlan).Sum(t => t.EffectiveAchieved);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            Assert.True(File.Exists(_filePath));

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();
            var totalRow = FindRowByLabel(sheet, "الإجمالي العام");

            Assert.Equal(expectedPlan, sheet.Cell(totalRow, ColPlan).GetValue<int>());
            Assert.Equal(expectedAchieved, sheet.Cell(totalRow, ColAchieved).GetValue<int>());
            Assert.True(expectedAchieved >= 170); // 150 محقق يدوي + 20 تصليح، على الأقل
        }

        [Fact]
        public void Export_throws_when_there_is_nothing_to_export()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<MonthlyPlanExcelService>(scope);

            Assert.Throws<InvalidOperationException>(() =>
                service.Export(new List<Business.DTOs.MonthlyPlanTrackingDto>(), "يوليو 2026", _filePath));
        }

        // ═══════════ Ach % (خام) مختلفة عمدًا عن نسبة المحقق (pro-rated) ═══════════

        [Fact]
        public async Task AchPercent_and_ProRatedPercent_are_genuinely_different_columns()
        {
            // أول الشهر عمدًا (مش Today، اللي واقع آخر يوليو) — عشان نسبة المحقق
            // pro-rated (مقسومة على أيام قليلة منقضية) تختلف بوضوح عن Ach% الخام
            var earlyInMonth = new DateTime(Year, Month, 2);

            using (var scope = _db.CreateScope())
            {
                await _db.GetService<MonthlyPlanTrackingService>(scope).SetDailyEntryAsync(TestDatabase.ProductChainId, earlyInMonth, 100);
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 1000);
            }

            var tracking = await _db.InScopeAsync<MonthlyPlanTrackingService, List<Business.DTOs.MonthlyPlanTrackingDto>>(
                s => s.GetTrackingAsync(Year, Month, earlyInMonth));
            var row = tracking.Single(t => t.ProductId == TestDatabase.ProductChainId);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();
            var productRow = FindRowByLabel(sheet, row.ProductName);

            var achPercent = sheet.Cell(productRow, ColAchPercent).GetValue<double>();
            var proRated = sheet.Cell(productRow, ColProRated).GetValue<double>();

            Assert.Equal(0.1, achPercent, 3); // 100 ÷ 1000 خام
            Assert.Equal((double)row.AchievedPercent!.Value, proRated, 3); // pro-rated لأيام الشهر المنقضية — رقم مختلف
            Assert.NotEqual(achPercent, proRated, 3);
        }

        // ═══════════ المطلوب يوميًا على مستوى العيلة — نفس صيغة الشاشة بالحرف ═══════════

        [Fact]
        public async Task FamilyRequiredDailyOutput_matches_the_shared_formula_not_a_naive_per_product_sum()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عيلة تصدير"));
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductRingId))!.FamilyId = familyId;
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
            {
                var svc = _db.GetService<MonthlyPlanService>(scope);
                await svc.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 10);
                await svc.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 10);
            }

            var tracking = await GetTrackingAsync();
            var remainingWorkdays = tracking[0].RemainingWorkdays;

            // الصيغة الصحيحة: على مستوى العيلة كلها، مش مجموع أرقام منتجات
            // مقرّبة كل واحدة لوحدها (باگ التقريب/التوزيع في الشيت القديم)
            var expected = MonthlyPlanFamilyMath.RequiredDailyOutput(planSum: 20, achievedSum: 0, remainingWorkdays);
            var naivePerProductSum = tracking
                .Where(t => t.ProductId is TestDatabase.ProductRingId or TestDatabase.ProductChainId)
                .Sum(t => t.RequiredDailyOutput ?? 0);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();
            var familyRow = FindRowByLabel(sheet, "إجمالي عيلة تصدير");
            var exported = sheet.Cell(familyRow, ColRequiredDaily).GetValue<int>();

            // القيمة المُصدَّرة لازم تطابق نفس صيغة الشاشة بالضبط (المدخلات الحقيقية)
            Assert.Equal(expected, exported);
            // وتوثيقًا للفرق: لو الأيام المتبقية مش قاسمة بالظبط، الجمع الساذج لأرقام مقرّبة يختلف عن الصيغة الصحيحة
            if (remainingWorkdays > 1 && 10 % remainingWorkdays != 0)
                Assert.NotEqual(naivePerProductSum, exported);
        }

        // ═══════════ خطة العيلة في التصدير = مجموع حي، مش رقم مكرر ═══════════

        [Fact]
        public async Task FamilyPlanTotal_in_export_is_recomputed_every_time_not_a_stale_copy()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عيلة الخطة الحية"));
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 100); return true; });

            var firstExportPath = Path.Combine(Path.GetTempPath(), $"wfm-plan-export-{Guid.NewGuid():N}.xlsx");
            try
            {
                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", firstExportPath);

                using (var wb1 = new XLWorkbook(firstExportPath))
                {
                    var familyRow1 = FindRowByLabel(wb1.Worksheets.First(), "إجمالي عيلة الخطة الحية");
                    Assert.Equal(100, wb1.Worksheets.First().Cell(familyRow1, ColPlan).GetValue<int>());
                }

                // الخطة اتغيّرت — التصدير الجديد لازم يعكسها، مش يفضل على القيمة القديمة
                await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
                { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 999); return true; });

                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", _filePath);

                using var wb2 = new XLWorkbook(_filePath);
                var familyRow2 = FindRowByLabel(wb2.Worksheets.First(), "إجمالي عيلة الخطة الحية");
                Assert.Equal(999, wb2.Worksheets.First().Cell(familyRow2, ColPlan).GetValue<int>());
            }
            finally
            {
                if (File.Exists(firstExportPath)) File.Delete(firstExportPath);
            }
        }

        // ═══════════ أيام الشغل في التصدير محسوبة فعليًا، مش رقم مكتوب ═══════════

        [Fact]
        public async Task WorkdaysFooter_reflects_the_real_computed_value_and_changes_with_a_holiday()
        {
            await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
            { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 10); return true; });

            var beforePath = Path.Combine(Path.GetTempPath(), $"wfm-plan-export-{Guid.NewGuid():N}.xlsx");
            try
            {
                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", beforePath);

                int totalBefore;
                using (var wb = new XLWorkbook(beforePath))
                {
                    var footerRow = FindRowByLabel(wb.Worksheets.First(), "أيام الشغل");
                    totalBefore = int.Parse(wb.Worksheets.First().Cell(footerRow, ColPlan).GetString().Split(':')[1].Trim());
                }

                using (var scope = _db.CreateScope())
                    await _db.GetService<MonthlyPlanTrackingService>(scope).AddHolidayAsync(new DateTime(Year, Month, 30));

                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", _filePath);

                using var workbook = new XLWorkbook(_filePath);
                var footerRowAfter = FindRowByLabel(workbook.Worksheets.First(), "أيام الشغل");
                var totalAfter = int.Parse(workbook.Worksheets.First().Cell(footerRowAfter, ColPlan).GetString().Split(':')[1].Trim());

                Assert.Equal(totalBefore - 1, totalAfter); // العطلة الجديدة قللت أيام الشغل الكلية
            }
            finally
            {
                if (File.Exists(beforePath)) File.Delete(beforePath);
            }
        }

        // ═══════════ الوزن مجموع حسابي صحيح، مش SUM على مدى فيه فراغات ═══════════

        [Fact]
        public async Task WeightSubtotal_sums_only_products_with_a_registered_weight()
        {
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.PieceWeightGrams = 10m;
                (await db.Products.FindAsync(TestDatabase.ProductRingId))!.PieceWeightGrams = null; // بلا وزن، عمدًا
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
            {
                var trackingSvc = _db.GetService<MonthlyPlanTrackingService>(scope);
                await trackingSvc.SetDailyEntryAsync(TestDatabase.ProductChainId, Today, 50);
                await trackingSvc.SetDailyEntryAsync(TestDatabase.ProductRingId, Today, 30);
                var planSvc = _db.GetService<MonthlyPlanService>(scope);
                await planSvc.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 100);
                await planSvc.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 100);
            }

            var tracking = await GetTrackingAsync();

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();
            var totalRow = FindRowByLabel(sheet, "الإجمالي العام");

            // بس المنتج اللي ليه وزن (10جم × 50 = 500جم = 0.5كجم) — مش المنتج التاني من غير وزن
            Assert.Equal(0.5m, sheet.Cell(totalRow, ColWeight).GetValue<decimal>());
        }

        // ═══════════ منتجات خارج الخطة في قسمها المستقل، مش داخل التجميع العادي ═══════════

        [Fact]
        public async Task OutsidePlanProducts_appear_in_their_own_section_not_inside_a_family_group()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عيلة عادية"));
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                (await db.Products.FindAsync(TestDatabase.ProductChainId))!.FamilyId = familyId;
                await db.SaveChangesAsync();
            }

            // ProductChainId: له خطة عادية. ProductRingId: إنتاج بس من غير خطة — خارج الخطة
            using (var scope = _db.CreateScope())
            {
                var trackingSvc = _db.GetService<MonthlyPlanTrackingService>(scope);
                await trackingSvc.SetDailyEntryAsync(TestDatabase.ProductChainId, Today, 50);
                await trackingSvc.SetDailyEntryAsync(TestDatabase.ProductRingId, Today, 25);
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 100);
            }

            var tracking = await GetTrackingAsync();
            var ringName = tracking.Single(t => t.ProductId == TestDatabase.ProductRingId).ProductName;
            Assert.True(tracking.Single(t => t.ProductId == TestDatabase.ProductRingId).IsOutsidePlan);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();

            var sectionHeaderRow = FindRowByLabel(sheet, "منتجات خارج الخطة");
            var ringRow = FindRowByLabel(sheet, ringName);
            var familyRow = FindRowByLabel(sheet, "عيلة عادية");

            Assert.True(ringRow > sectionHeaderRow); // تحت قسمه المستقل بعد الإجمالي العام
            Assert.True(ringRow > familyRow); // مش جوه مجموعة العيلة العادية
            Assert.Equal(25, sheet.Cell(ringRow, ColAchieved).GetValue<int>());
        }
    }
}
