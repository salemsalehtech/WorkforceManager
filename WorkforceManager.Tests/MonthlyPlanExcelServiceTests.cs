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
            var sheet = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);
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
            var sheet = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);
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
            var sheet = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);
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
                    var familyRow1 = FindRowByLabel(wb1.Worksheet(MonthlyPlanExcelService.DetailSheetName), "إجمالي عيلة الخطة الحية");
                    Assert.Equal(100, wb1.Worksheet(MonthlyPlanExcelService.DetailSheetName).Cell(familyRow1, ColPlan).GetValue<int>());
                }

                // الخطة اتغيّرت — التصدير الجديد لازم يعكسها، مش يفضل على القيمة القديمة
                await _db.InScopeAsync<MonthlyPlanService, bool>(async s =>
                { await s.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 999); return true; });

                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", _filePath);

                using var wb2 = new XLWorkbook(_filePath);
                var familyRow2 = FindRowByLabel(wb2.Worksheet(MonthlyPlanExcelService.DetailSheetName), "إجمالي عيلة الخطة الحية");
                Assert.Equal(999, wb2.Worksheet(MonthlyPlanExcelService.DetailSheetName).Cell(familyRow2, ColPlan).GetValue<int>());
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
                    var footerRow = FindRowByLabel(wb.Worksheet(MonthlyPlanExcelService.DetailSheetName), "أيام الشغل");
                    totalBefore = int.Parse(wb.Worksheet(MonthlyPlanExcelService.DetailSheetName).Cell(footerRow, ColPlan).GetString().Split(':')[1].Trim());
                }

                using (var scope = _db.CreateScope())
                    await _db.GetService<MonthlyPlanTrackingService>(scope).AddHolidayAsync(new DateTime(Year, Month, 30));

                using (var scope = _db.CreateScope())
                    _db.GetService<MonthlyPlanExcelService>(scope).Export(await GetTrackingAsync(), "يوليو 2026", _filePath);

                using var workbook = new XLWorkbook(_filePath);
                var footerRowAfter = FindRowByLabel(workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName), "أيام الشغل");
                var totalAfter = int.Parse(workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName).Cell(footerRowAfter, ColPlan).GetString().Split(':')[1].Trim());

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
            var sheet = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);
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
            var sheet = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);

            var sectionHeaderRow = FindRowByLabel(sheet, "منتجات خارج الخطة");
            var ringRow = FindRowByLabel(sheet, ringName);
            var familyRow = FindRowByLabel(sheet, "عيلة عادية");

            Assert.True(ringRow > sectionHeaderRow); // تحت قسمه المستقل بعد الإجمالي العام
            Assert.True(ringRow > familyRow); // مش جوه مجموعة العيلة العادية
            Assert.Equal(25, sheet.Cell(ringRow, ColAchieved).GetValue<int>());
        }

        // ═══════════ الشيت الأول: نفس أعمدة شيت المصنع بمعادلات حية ═══════════

        // ترتيب شيت المصنع: العيلة، المنتج، الخطة، المخطط، المحقق، إنتاج اليوم، Ach %، نسبة المحقق،
        // وزن القطعة، إجمالي الوزن، إجمالي الإنتاج اليومي، تصليحات
        private const int FFamily = 1, FProduct = 2, FFamilyPlan = 3, FPlan = 4, FAchieved = 5, FToday = 6,
            FAch = 7, FProRated = 8, FPieceWeight = 9, FTotalWeight = 10, FFamilyToday = 11, FCorrections = 12;

        private static int FindRow(IXLWorksheet sheet, int column, string text)
        {
            var lastRow = sheet.LastRowUsed()!.RowNumber();
            for (var r = 1; r <= lastRow; r++)
                if (sheet.Cell(r, column).GetString() == text) return r;
            throw new InvalidOperationException($"'{text}' مش موجود في العمود {column}");
        }

        [Fact]
        public async Task FactorySheet_has_the_factory_column_order_and_live_formulas()
        {
            var familyId = await _db.InScopeAsync<ProductFamilyService, int>(s => s.CreateAsync("عقله 29"));
            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                var chain = (await db.Products.FindAsync(TestDatabase.ProductChainId))!;
                var ring = (await db.Products.FindAsync(TestDatabase.ProductRingId))!;
                chain.FamilyId = familyId; chain.PieceWeightGrams = 77m; chain.Material = Core.Enums.Material.Copper;
                ring.FamilyId = familyId; ring.Material = Core.Enums.Material.Copper;
                await db.SaveChangesAsync();
            }

            using (var scope = _db.CreateScope())
            {
                var plan = _db.GetService<MonthlyPlanService>(scope);
                await plan.SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 1000);
                await plan.SetPlanAsync(TestDatabase.ProductRingId, Year, Month, 500);
                var tracking = _db.GetService<MonthlyPlanTrackingService>(scope);
                await tracking.SetDailyEntryAsync(TestDatabase.ProductChainId, Today, 600);
                await tracking.SetDailyEntryAsync(TestDatabase.ProductRingId, Today, 100);
            }

            var rows = await GetTrackingAsync();
            var chainRow = rows.Single(t => t.ProductId == TestDatabase.ProductChainId);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(rows, "يوليو 2026", _filePath,
                    new Business.DTOs.ReportExportOptions { DepartmentName = "الصنفرة" });

            using var workbook = new XLWorkbook(_filePath);
            Assert.Equal(new[] { "اللوحة", "الخطة الشهرية", "تفاصيل" }, workbook.Worksheets.Select(w => w.Name));
            var sheet = workbook.Worksheet("الخطة الشهرية");

            var headerRow = FindRow(sheet, FProduct, "المنتج");
            Assert.Equal(
                new[] { "العيلة", "المنتج", "الخطة", "المخطط", "المحقق", "إنتاج اليوم", "Ach %", "نسبة المحقق",
                        "وزن القطعة (كجم)", "إجمالي الوزن (كجم)", "إجمالي الإنتاج اليومي", "تصليحات" },
                Enumerable.Range(1, 12).Select(c => sheet.Cell(headerRow, c).GetString()));
            Assert.Equal(1, FindRow(sheet, 1, "قسم الصنفرة"));

            var r = FindRow(sheet, FProduct, chainRow.ProductName);
            Assert.True(sheet.Cell(r, FAch).HasFormula);
            Assert.True(sheet.Cell(r, FProRated).HasFormula);
            Assert.True(sheet.Cell(r, FTotalWeight).HasFormula);

            Assert.Equal(0.6, sheet.Cell(r, FAch).GetValue<double>(), 3);                           // 600 ÷ 1000
            Assert.Equal((double)chainRow.AchievedPercent!.Value, sheet.Cell(r, FProRated).GetValue<double>(), 3); // نفس رقم الشاشة
            Assert.Equal(46.2, sheet.Cell(r, FTotalWeight).GetValue<double>(), 3);                  // 600 × 0.077
            Assert.Equal(600, sheet.Cell(r, FToday).GetValue<int>());

            // الخطة وإجمالي الإنتاج اليومي على مستوى العيلة = SUM منتجاتها (خلية مدمجة)
            var familyFirstRow = sheet.Cell(r, FFamily).MergedRange().FirstCell().Address.RowNumber;
            Assert.Equal("عقله 29", sheet.Cell(familyFirstRow, FFamily).GetString());
            Assert.Equal(1500, sheet.Cell(familyFirstRow, FFamilyPlan).GetValue<int>());
            Assert.Equal(700, sheet.Cell(familyFirstRow, FFamilyToday).GetValue<int>());

            var copperTotal = FindRow(sheet, FFamily, "إجمالي محقق النحاس");
            var grand = FindRow(sheet, FFamily, "الإجمالي العام");
            Assert.Equal(700, sheet.Cell(copperTotal, FAchieved).GetValue<int>());
            Assert.Equal(rows.Where(t => !t.IsOutsidePlan).Sum(t => t.PlannedQuantity), sheet.Cell(grand, FPlan).GetValue<int>());
            Assert.Equal(rows.Where(t => !t.IsOutsidePlan).Sum(t => t.EffectiveAchieved), sheet.Cell(grand, FAchieved).GetValue<int>());

            var workdaysRow = FindRow(sheet, FFamily, "باقي الأيام");
            Assert.Equal(chainRow.RemainingWorkdays, sheet.Cell(workdaysRow, FFamilyPlan).GetValue<int>());
        }

        [Fact]
        public async Task FactorySheet_ratios_recalculate_when_the_plan_is_edited_in_excel()
        {
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 1000);
                await _db.GetService<MonthlyPlanTrackingService>(scope).SetDailyEntryAsync(TestDatabase.ProductChainId, Today, 500);
            }
            var rows = await GetTrackingAsync();
            var name = rows.Single(t => t.ProductId == TestDatabase.ProductChainId).ProductName;

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(rows, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheet("الخطة الشهرية");
            var r = FindRow(sheet, FProduct, name);

            sheet.Cell(r, FPlan).Value = 2000; // المستخدم عدّل المخطط في الإكسل
            workbook.RecalculateAllFormulas();

            Assert.Equal(0.25, sheet.Cell(r, FAch).GetValue<double>(), 3);
        }

        // ═══════════ يوم بيوم + اللوحة + الحماية + التجميع ═══════════

        private async Task<(List<Business.DTOs.MonthlyPlanTrackingDto> Rows, Business.DTOs.MonthlyPlanDailyBreakdownDto Daily)> SeedTwoDaysAsync()
        {
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 2600);
                var tracking = _db.GetService<MonthlyPlanTrackingService>(scope);
                await tracking.SetDailyEntryAsync(TestDatabase.ProductChainId, new DateTime(Year, Month, 1), 100);
                await tracking.SetDailyEntryAsync(TestDatabase.ProductChainId, new DateTime(Year, Month, 2), 200);
                await tracking.SetCorrectionAsync(TestDatabase.ProductChainId, new DateTime(Year, Month, 2), 10);
            }
            var rows = await GetTrackingAsync();
            var daily = await _db.InScopeAsync<MonthlyPlanTrackingService, Business.DTOs.MonthlyPlanDailyBreakdownDto>(
                s => s.GetDailyBreakdownAsync(Year, Month, Today));
            return (rows, daily);
        }

        [Fact]
        public async Task DailySheet_spreads_achieved_over_days_and_totals_match()
        {
            var (rows, daily) = await SeedTwoDaysAsync();
            var chainName = rows.Single(t => t.ProductId == TestDatabase.ProductChainId).ProductName;

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(rows, "يوليو 2026", _filePath, daily: daily);

            using var workbook = new XLWorkbook(_filePath);
            Assert.Equal(new[] { "اللوحة", "الخطة الشهرية", "تفاصيل", "يوم بيوم" }, workbook.Worksheets.Select(w => w.Name));

            var sheet = workbook.Worksheet("يوم بيوم");
            var r = FindRow(sheet, 1, chainName);
            Assert.Equal(100, sheet.Cell(r, 3).GetValue<int>());  // يوم 1
            Assert.Equal(210, sheet.Cell(r, 4).GetValue<int>());  // يوم 2 = 200 + تصليح 10
            Assert.Equal(310, sheet.Cell(r, 3 + 31).GetValue<int>()); // عمود الإجمالي

            var totalRow = FindRow(sheet, 1, "إجمالي اليوم");
            Assert.Equal(rows.Sum(t => t.EffectiveAchieved), sheet.Cell(totalRow, 3 + 31).GetValue<int>());
        }

        [Fact]
        public async Task Dashboard_kpis_are_formulas_over_the_plan_sheet()
        {
            var (rows, daily) = await SeedTwoDaysAsync();

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(rows, "يوليو 2026", _filePath, daily: daily);

            using var workbook = new XLWorkbook(_filePath);
            var dashboard = workbook.Worksheet("اللوحة");
            var labelRow = FindRow(dashboard, 1, "نسبة الإنجاز");
            var completion = dashboard.Cell(labelRow + 1, 1);

            var planned = rows.Where(t => !t.IsOutsidePlan).Sum(t => t.PlannedQuantity);
            var achieved = rows.Where(t => !t.IsOutsidePlan).Sum(t => t.EffectiveAchieved);
            Assert.True(completion.HasFormula);
            Assert.Equal((double)achieved / planned, completion.GetValue<double>(), 4);
        }

        [Fact]
        public async Task PlanSheet_is_protected_except_raw_inputs_and_detail_families_are_grouped()
        {
            var (rows, _) = await SeedTwoDaysAsync();
            var chainName = rows.Single(t => t.ProductId == TestDatabase.ProductChainId).ProductName;

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(rows, "يوليو 2026", _filePath);

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheet("الخطة الشهرية");
            Assert.True(sheet.IsProtected);
            var r = FindRow(sheet, FProduct, chainName);
            Assert.False(sheet.Cell(r, FPlan).Style.Protection.Locked);   // الخطة بتتعدّل
            Assert.True(sheet.Cell(r, FAch).Style.Protection.Locked);     // المعادلة مقفولة

            var detail = workbook.Worksheet(MonthlyPlanExcelService.DetailSheetName);
            var detailRow = FindRowByLabel(detail, chainName);
            Assert.True(detail.Row(detailRow).OutlineLevel > 0);
        }
    }
}
