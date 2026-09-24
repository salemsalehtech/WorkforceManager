using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using WorkforceManager.Business.Services;
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

        [Fact]
        public async Task Export_totals_match_the_same_tracking_data_the_screen_reads()
        {
            using (var scope = _db.CreateScope())
            {
                await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.ChainStage1Id, 150, Today, confirmOverride: true);
                await _db.GetService<MonthlyPlanService>(scope).SetPlanAsync(TestDatabase.ProductChainId, Year, Month, 500);
                await _db.GetService<MonthlyPlanTrackingService>(scope).SetCorrectionAsync(TestDatabase.ProductChainId, Today, 20);
            }

            var tracking = await _db.InScopeAsync<MonthlyPlanTrackingService,
                List<Business.DTOs.MonthlyPlanTrackingDto>>(s => s.GetTrackingAsync(Year, Month, Today));

            var expectedPlan = tracking.Sum(t => t.PlannedQuantity);
            var expectedAchieved = tracking.Sum(t => t.EffectiveAchieved);

            using (var scope = _db.CreateScope())
                _db.GetService<MonthlyPlanExcelService>(scope).Export(tracking, "يوليو 2026", _filePath);

            Assert.True(File.Exists(_filePath));

            using var workbook = new XLWorkbook(_filePath);
            var sheet = workbook.Worksheets.First();

            // آخر صف فيه بيانات هو "الإجمالي العام" — عمود 2 (مخطط) وعمود 3 (محقق)
            var lastRow = sheet.LastRowUsed()!.RowNumber();
            var totalLabel = sheet.Cell(lastRow, 1).GetString();
            var totalPlan = sheet.Cell(lastRow, 2).GetValue<int>();
            var totalAchieved = sheet.Cell(lastRow, 3).GetValue<int>();

            Assert.Equal("الإجمالي العام", totalLabel);
            Assert.Equal(expectedPlan, totalPlan);
            Assert.Equal(expectedAchieved, totalAchieved);
            Assert.True(expectedAchieved >= 170); // 150 محقق حقيقي + 20 تصليح، على الأقل (منتجات تانية ممكن تكون فيها نشاط من سييد تاني)
        }

        [Fact]
        public void Export_throws_when_there_is_nothing_to_export()
        {
            using var scope = _db.CreateScope();
            var service = _db.GetService<MonthlyPlanExcelService>(scope);

            Assert.Throws<InvalidOperationException>(() =>
                service.Export(new List<Business.DTOs.MonthlyPlanTrackingDto>(), "يوليو 2026", _filePath));
        }
    }
}
