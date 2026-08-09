using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// مُنشئ التقارير: المستخدم بيختار موضوع ومدة وتفصيل، والباقي
    /// بيتبني لوحده.
    ///
    /// أخطر حاجة في الشغل ده مش إن التقرير يقع — إنه يطلع **رقم
    /// مختلف** عن الشاشة اللي بتعرض نفس الحاجة. فأهم اختبارات هنا
    /// بتقارن ناتج المُنشئ بناتج الخدمات المعتمدة (الأجور، اليوميات،
    /// خصم الغياب) وتتأكد إنهم بيقولوا نفس الرقم بالحرف.
    /// </summary>
    public class ReportBuilderTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task RecordAsync(int workerId, int stageId, int pieces, DateTime? date = null)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<WorkdayCalculationService>(scope).RecordProductionAsync(
                workerId, stageId, pieces, date ?? Day, confirmOverride: true);
        }

        private async Task<ReportTable> BuildAsync(ReportSpec spec)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<ReportBuilderService>(scope).BuildAsync(spec);
        }

        private static ReportSpec Spec(
            ReportSubject subject, ReportGrouping groupBy,
            DateTime? from = null, DateTime? to = null) => new()
            {
                Subject = subject,
                GroupBy = groupBy,
                From = from ?? Day,
                To = to ?? Day
            };

        // ======================= الشكل العام =======================

        [Fact]
        public async Task Every_report_comes_back_with_a_title_a_period_and_a_totals_row()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));

            Assert.False(string.IsNullOrWhiteSpace(table.Title));
            Assert.Contains("2026", table.PeriodText);
            Assert.NotNull(table.Totals);
        }

        [Fact]
        public async Task The_totals_row_only_sums_the_columns_that_can_be_summed()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage1Id, 50);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));

            // القطع بتتجمع
            var pieces = table.Columns.FindIndex(c => c.Header == "القطع");
            Assert.Equal(150, table.Totals!.Values[pieces]);

            // "عدد العمال" مبيتجمعش — جمع عدد العمال عبر الصفوف رقم مالوش معنى
            var workers = table.Columns.FindIndex(c => c.Header == "عدد العمال");
            Assert.False(table.Columns[workers].Sums);
            Assert.Null(table.Totals.Values[workers]);
        }

        [Fact]
        public async Task An_empty_period_comes_back_empty_not_broken()
        {
            var table = await BuildAsync(Spec(
                ReportSubject.Production, ReportGrouping.Worker,
                Day.AddDays(-60), Day.AddDays(-50)));

            Assert.True(table.IsEmpty);
            Assert.Null(table.Totals);
        }

        // ======================= نفس الرقم زي الشاشات =======================

        [Fact]
        public async Task The_wages_report_says_exactly_what_the_payroll_service_says()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 60);

            decimal expected;
            using (var scope = _db.CreateScope())
                expected = (await _db.GetService<PayrollService>(scope)
                    .GetPeriodPayrollAsync(Day, Day)).TotalWageEgp;

            var table = await BuildAsync(Spec(ReportSubject.Wages, ReportGrouping.Worker));
            var wage = table.Columns.FindIndex(c => c.Header == "الأجر النهائي");

            Assert.Equal(expected, table.Totals!.Values[wage]);
        }

        [Fact]
        public async Task The_production_report_says_exactly_what_the_records_say()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 100);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));
            var pieces = table.Columns.FindIndex(c => c.Header == "القطع");

            // 200 مش 100: التقرير ده بيقيس شغل العامل، والقطعة اللي عدّت
            // على مرحلتين اشتغل فيها مرتين
            Assert.Equal(200, table.Totals!.Values[pieces]);
        }

        [Fact]
        public async Task The_attendance_report_uses_the_one_absence_deduction_rule()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<AttendanceService>(scope).RecordAttendanceBatchAsync(
                    Day, new[] { (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission) });

            var table = await BuildAsync(Spec(ReportSubject.Attendance, ReportGrouping.Worker));
            var deduction = table.Columns.FindIndex(c => c.Header.StartsWith("خصم الغياب"));

            Assert.Equal(AbsenceDeductionRule.UnexcusedAbsencePerDay, table.Totals!.Values[deduction]);
        }

        // ======================= التجميعات =======================

        [Fact]
        public async Task Grouping_by_worker_gives_one_row_per_worker()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 40);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage3Id, 30);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));

            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(140, table.Rows.Single(r => r.Label == "أحمد").Values[0]);
        }

        [Fact]
        public async Task Grouping_by_stage_names_the_product_with_the_stage()
        {
            // اسم المرحلة لوحده مش كافي: "قص" موجودة في أكتر من منتج
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Stage));

            Assert.Contains("شنطة", table.Rows[0].Label);
            Assert.Contains("قص", table.Rows[0].Label);
        }

        [Fact]
        public async Task Grouping_by_product_counts_the_last_stage_only_not_every_stage()
        {
            // نفس الدفعة عدّت على تلات مراحل (قص، خياطة، تشطيب) بنفس
            // العدد — لو "القطع" بتجمع المراحل الثلاثة هتبان 66 قطعة
            // تامة، والحقيقة إن 22 قطعة بس خرجت من الخط. آخر مرحلة
            // (تشطيب) هي الوحيدة اللي بتحسب هنا، زي تقرير الإنتاج
            // العام والرسم البياني (<see cref="ProductionLine"/>).
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 22);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Product));

            var row = table.Rows.Single(r => r.Label == "شنطة");
            Assert.Equal(22, row.Values[0]);
        }

        [Fact]
        public async Task Grouping_by_day_gives_one_row_per_day_in_order()
        {
            // على آخر مرحلة (تشطيب) عشان الأرقام تبان في عمود القطع —
            // "الإنتاج باليوم" بيعدّ التام زي "بالمنتج" بالظبط
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 10, Day);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 20, Day.AddDays(1));
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 30, Day.AddDays(2));

            var table = await BuildAsync(Spec(
                ReportSubject.Production, ReportGrouping.Day, Day, Day.AddDays(2)));

            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(10, table.Rows[0].Values[0]);
            Assert.Equal(30, table.Rows[2].Values[0]);
        }

        [Fact]
        public async Task Grouping_by_day_counts_what_left_the_line_not_every_stage()
        {
            // نفس الدفعة عدّت على تلات مراحل في نفس اليوم — 22 خرجت
            // تامة، مش 66. نفس قاعدة الرسم البياني وتقرير اليوم.
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 22);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Day));

            Assert.Equal(22, Assert.Single(table.Rows).Values[0]);
        }

        [Fact]
        public async Task Grouping_by_worker_still_counts_every_stage_he_worked_on()
        {
            // الفرق عن اللي فوق: العامل وحدة **شغل** مش وعاء إنتاج.
            // اشتغل تلات مراحل × 22 = 66 قطعة شغل، وكل واحدة مستحق
            // عليها يوميتها. الجمع هنا صح ومقصود.
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 22);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 22);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));

            Assert.Equal(66, Assert.Single(table.Rows).Values[0]);
        }

        // ======================= الفلاتر =======================

        [Fact]
        public async Task Filtering_by_worker_leaves_the_others_out()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 50);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day,
                WorkerIds = new[] { TestDatabase.WorkerAhmedId }
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal("أحمد", row.Label);
        }

        [Fact]
        public async Task Filtering_by_product_leaves_the_others_out()
        {
            // على آخر مرحلة بتاعة كل منتج عن قصد — دبلة آخر مرحلة ليها
            // تلميع (RingStage2Id)، والقطع بتحسب من هناك بس لما التجميع
            // بالمنتج (شوف Grouping_by_product_counts_the_last_stage_only)
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.RingStage2Id, 70);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Product,
                From = Day,
                To = Day,
                ProductIds = new[] { TestDatabase.ProductRingId }
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal("دبلة", row.Label);
            Assert.Equal(70, row.Values[0]);
        }

        [Fact]
        public async Task An_empty_filter_list_means_the_filter_is_off_not_match_nothing()
        {
            // نفس قاعدة WorkerFilterRules في شاشة العمال
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day,
                WorkerIds = Array.Empty<int>()
            });

            Assert.NotEmpty(table.Rows);
        }

        // ======================= التركيبات المسموحة =======================

        [Fact]
        public void Attendance_cannot_be_grouped_by_product_because_that_has_no_meaning()
        {
            Assert.False(ReportSpec.IsAllowed(ReportSubject.Attendance, ReportGrouping.Product));
            Assert.True(ReportSpec.IsAllowed(ReportSubject.Attendance, ReportGrouping.Worker));
        }

        [Fact]
        public void Production_can_be_cut_every_way()
        {
            foreach (var grouping in Enum.GetValues<ReportGrouping>())
                Assert.True(ReportSpec.IsAllowed(ReportSubject.Production, grouping));
        }

        [Fact]
        public void Skills_ignore_the_period_because_they_are_a_state_not_a_movement()
        {
            Assert.False(ReportSpec.UsesPeriod(ReportSubject.Skills));
            Assert.True(ReportSpec.UsesPeriod(ReportSubject.Production));
        }

        [Fact]
        public void Every_subject_offers_at_least_one_grouping()
        {
            foreach (var subject in Enum.GetValues<ReportSubject>())
                Assert.NotEmpty(ReportSpec.AllowedGroupings(subject));
        }

        // ======================= المدة السابقة =======================

        [Fact]
        public void The_previous_period_is_the_same_length_ending_the_day_before()
        {
            var spec = new ReportSpec { From = new DateTime(2026, 3, 1), To = new DateTime(2026, 3, 31) };
            var (from, to) = spec.PreviousPeriod();

            Assert.Equal(new DateTime(2026, 2, 28), to);       // اليوم اللي قبل البداية
            Assert.Equal(31, (to - from).Days + 1);            // نفس الطول بالظبط
        }

        // ======================= المهارات =======================

        [Fact]
        public async Task Skills_by_stage_answers_how_many_workers_can_do_it()
        {
            var table = await BuildAsync(Spec(ReportSubject.Skills, ReportGrouping.Stage));

            var row = table.Rows.Single(r => r.Label.Contains("قص"));

            // العاملين المزروعين الاتنين مؤهلين لكل المراحل
            Assert.Equal(2, row.Values[0]);
            await Task.CompletedTask;
        }
    }
}
