using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Models;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات قاعدة تحويل القطع ليوميات.
    ///
    /// الرقم ده بيتضرب في سعر اليومية وبيطلع أجر العامل، فأي اختلاف فيه
    /// بين شاشتين معناه إن البرنامج بيقول للمستخدم رقمين لنفس الشغل.
    /// وده اللي كان بيحصل: معاينة الحفظ كانت بتجمع الكسور الكاملة وتقرّب
    /// مرة واحدة، وباقي البرنامج بيقرّب كل سجل لوحده وبعدين يجمع.
    ///
    /// منتج "تلاتات" (كوتة 3) موجود في بيانات الاختبار عشان الحالة دي
    /// بالذات: 1 ÷ 3 = 0.3333 فالتقريب بيبان. باقي الكوتات 10 والقسمة
    /// عليها تامة، فالفرق مكانش ممكن يتمسك بيها.
    /// </summary>
    public class WorkdayRoundingTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static readonly int[] ThirdsLine =
        {
            TestDatabase.ThirdsStage1Id, TestDatabase.ThirdsStage2Id, TestDatabase.ThirdsStage3Id
        };

        // ======================= القاعدة نفسها =======================

        [Theory]
        [InlineData(1, 3, 0.33)]     // 0.3333... → لتحت
        [InlineData(2, 3, 0.67)]     // 0.6666... → لفوق
        [InlineData(100, 10, 10.00)] // قسمة تامة
        [InlineData(5, 10, 0.5)]
        public void Workdays_are_rounded_to_two_decimals(int pieces, int quota, decimal expected)
        {
            Assert.Equal(expected, WorkdayMath.FromPieces(pieces, quota));
        }

        [Fact]
        public void Zero_quota_yields_zero_instead_of_throwing()
        {
            // التحقق بيمنع اليومية صفر عند الإدخال، بس بيانات قديمة أو
            // متبوّظة ميصحش تكسر كشف الأجور كله
            Assert.Equal(0m, WorkdayMath.FromPieces(500, 0));
        }

        // ======================= الاتساق بين الشاشات =======================

        [Fact]
        public async Task Save_preview_matches_what_the_saved_records_report()
        {
            // 3 مراحل × قطعة واحدة، كوتة 3 → كل سجل 0.33 يومية.
            // قبل الإصلاح: المعاينة كانت بتجمع 0.3333×3 = 0.9999 وتقرّب = 1.00
            // بينما السجلات المحفوظة بتقول 0.33×3 = 0.99 — رقمين لنفس الشغل
            var range = new FlowRangeDto
            {
                FromStageId = TestDatabase.ThirdsStage1Id,
                ToStageId = TestDatabase.ThirdsStage3Id,
                PieceCount = 1
            };

            var shares = ThirdsLine.Select(stageId => new FlowShareDto
            {
                ProductionStageId = stageId,
                WorkerId = TestDatabase.WorkerAhmedId,
                PieceCount = 1
            }).ToList();

            FlowSaveResultDto result;
            using (var scope = _db.CreateScope())
                result = await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductThirdsId, TestDatabase.Today,
                    new[] { range }, shares, confirmOverride: true);

            var previewTotal = Assert.Single(result.WorkerTotals).TotalWorkdays;

            // نفس الرقم اللي الكشف الأسبوعي وكشف الأجور هيحسبوه من السجلات
            var savedTotal = (await _db.GetProductionAsync()).Sum(r => r.WorkdaysCompleted);

            Assert.Equal(0.99m, savedTotal);
            Assert.Equal(savedTotal, previewTotal);
        }

        [Fact]
        public async Task Weekly_sheet_and_payroll_agree_on_the_same_work()
        {
            // نفس السجلات، مسارين حساب مختلفين — لازم يطلعوا نفس الرقم،
            // لأن العامل بيتحاسب بالرقم ده
            var range = new FlowRangeDto
            {
                FromStageId = TestDatabase.ThirdsStage1Id,
                ToStageId = TestDatabase.ThirdsStage3Id,
                PieceCount = 2
            };

            var shares = ThirdsLine.Select(stageId => new FlowShareDto
            {
                ProductionStageId = stageId,
                WorkerId = TestDatabase.WorkerAhmedId,
                PieceCount = 2
            }).ToList();

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                    TestDatabase.ProductThirdsId, TestDatabase.Today,
                    new[] { range }, shares, confirmOverride: true);

            using var check = _db.CreateScope();

            var weekly = await _db.GetService<WeeklySummaryService>(check)
                .GetWorkerWeeklySummaryAsync(TestDatabase.WorkerAhmedId, TestDatabase.Today);

            var payroll = await _db.GetService<PayrollService>(check)
                .GetPeriodPayrollAsync(TestDatabase.Today, TestDatabase.Today);

            var payrollRow = payroll.Workers.Single(w => w.WorkerId == TestDatabase.WorkerAhmedId);

            Assert.Equal(2.01m, weekly!.ProducedWorkdays); // 0.67 × 3
            Assert.Equal(weekly.ProducedWorkdays, payrollRow.ProducedWorkdays);
        }
    }
}
