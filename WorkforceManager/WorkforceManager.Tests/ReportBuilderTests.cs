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

            // بالعامل: "عدد الضربات" مش "القطع" — رقم العامل عدد ضرباته
            // على المكنة، مش الإنتاج الفعلي للمنتج
            var pieces = table.Columns.FindIndex(c => c.Header == "عدد الضربات");
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
            var pieces = table.Columns.FindIndex(c => c.Header == "عدد الضربات");

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
        public async Task Grouping_by_month_collapses_days_into_one_row_per_month()
        {
            // Day = 29 يوليو 2026 — بعد 5 أيام بيدخل أغسطس، فده يعبر
            // حد شهر حقيقي من غير تاريخ مكتوب بإيد
            var august = Day.AddDays(5);

            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 10, Day);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage3Id, 20, august);

            var table = await BuildAsync(Spec(
                ReportSubject.Production, ReportGrouping.Month, Day, august));

            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(10, table.Rows[0].Values[0]);
            Assert.Equal(20, table.Rows[1].Values[0]);
            Assert.Equal("2026/07", table.Rows[0].Label);
            Assert.Equal("2026/08", table.Rows[1].Label);
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
        public async Task Filtering_the_wages_report_by_product_only_shows_who_worked_on_it()
        {
            // نفس فلتر المنتج/المرحلة لازم يشتغل في أي موضوع تقرير عنده
            // عمال، مش الإنتاج بس — قبل الإصلاح كان بيوصل لـAllowedWorkerIdsAsync
            // ومبيعملش حاجة خالص، فكشف الأجور كان بيرجّع كل العمال بصرف
            // النظر عن المنتج المُعلّم في الفلتر
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.RingStage2Id, 70);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Wages,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day,
                ProductIds = new[] { TestDatabase.ProductBagId }
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal("أحمد", row.Label);
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

        // ======================= شكل الجدول: أعمدة وترتيب =======================

        private static ReportSpec ProductionSpec(
            ReportGrouping groupBy = ReportGrouping.Worker,
            IReadOnlyList<ReportColumnChoice>? columns = null,
            string? sortKey = null,
            bool descending = true,
            int? topN = null) => new()
            {
                Subject = ReportSubject.Production,
                GroupBy = groupBy,
                From = Day,
                To = Day,
                ColumnLayout = columns,
                SortKey = sortKey,
                SortDescending = descending,
                TopN = topN
            };

        [Fact]
        public async Task HidingAColumn_RemovesItAndItsNumbers_NotJustTheHeader()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(ProductionSpec(columns: new[]
            {
                new ReportColumnChoice { Key = "pieces" },
                new ReportColumnChoice { Key = "workdays", Visible = false },
                new ReportColumnChoice { Key = "workers" },
                new ReportColumnChoice { Key = "workdays_with_work", Visible = false }
            }));

            // المخفي اختفى، والمذكور فضل بترتيبه
            Assert.DoesNotContain(table.Columns, c => c.Key == "workdays");
            Assert.DoesNotContain(table.Columns, c => c.Key == "workdays_with_work");
            Assert.Equal("pieces", table.Columns[0].Key);
            Assert.Equal("workers", table.Columns[1].Key);

            // العدد لازم يطابق عدد الأعمدة — صف فيه قيم زيادة معناه
            // الأرقام اتزحلقت تحت أعمدة غلط
            Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Values.Count));
            Assert.Equal(100, table.Rows[0].Values[0]);
        }

        [Fact]
        public async Task ReorderingColumns_MovesTheValuesWithThem()
        {
            // 100 قطعة = 10 يوميات (الكوتة 10) — رقمين مختلفين عشان
            // لو اتبدلوا الاختبار يقع
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            // محرّر الأعمدة بيبعت القايمة كاملة بترتيبها، فالاختبار
            // بيعمل زيه
            var table = await BuildAsync(ProductionSpec(columns: new[]
            {
                new ReportColumnChoice { Key = "workdays" },
                new ReportColumnChoice { Key = "pieces" },
                new ReportColumnChoice { Key = "workers" },
                new ReportColumnChoice { Key = "workdays_with_work" }
            }));

            // أول أربع أعمدة بالترتيب اللي المستخدم طلبه — واللي
            // ماتذكرش (الهالك ونسبته) بيتزوّد بعدهم
            Assert.Equal(
                new[] { "workdays", "pieces", "workers", "workdays_with_work" },
                table.Columns.Take(4).Select(c => c.Key));

            Assert.Equal(10, table.Rows[0].Values[0]);   // اليوميات
            Assert.Equal(100, table.Rows[0].Values[1]);  // القطع
        }

        [Fact]
        public async Task RenamingAColumn_KeepsItsKeyAndItsNumbers()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(ProductionSpec(columns: new[]
            {
                new ReportColumnChoice { Key = "pieces", Header = "الإنتاج التام" }
            }));

            var column = table.Columns.Single(c => c.Key == "pieces");
            Assert.Equal("الإنتاج التام", column.Header);
            Assert.Equal(100, table.Rows[0].Values[0]);
        }

        [Fact]
        public async Task AColumnTheLayoutNeverMentions_StaysVisible()
        {
            // قالب اتحفظ زمان وبعدين اتزوّد عمود جديد — مينفعش يختفي
            // من غير ما حد ياخد باله
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(ProductionSpec(columns: new[]
            {
                new ReportColumnChoice { Key = "pieces" }
            }));

            Assert.Contains(table.Columns, c => c.Key == "workdays");
        }

        [Fact]
        public async Task SortingByAColumn_OrdersTheRowsByIt()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 30);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 90);

            var table = await BuildAsync(ProductionSpec(sortKey: "pieces", descending: true));

            Assert.Equal("سعيد", table.Rows[0].Label);
            Assert.Equal("أحمد", table.Rows[1].Label);
        }

        [Fact]
        public async Task TopN_KeepsTheBestRows_AndTheTotalMatchesWhatIsShown()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 30);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 90);

            var table = await BuildAsync(ProductionSpec(sortKey: "pieces", descending: true, topN: 1));

            var row = Assert.Single(table.Rows);
            Assert.Equal("سعيد", row.Label);

            // الإجمالي تحت صف واحد لازم يساوي الصف ده — مش جمع الاتنين.
            // إجمالي بيقول 120 تحت صف بـ90 رقم بيكدّب نفسه.
            Assert.Equal(90, table.Totals!.Values[0]);
        }

        [Fact]
        public async Task SortingByAHiddenColumn_StillWorks()
        {
            // "رتّب بالأجر بس متعرضهوش" — الترتيب بيتنفّذ قبل الإخفاء
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 30);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 90);

            var table = await BuildAsync(ProductionSpec(
                columns: new[]
                {
                    new ReportColumnChoice { Key = "pieces", Visible = false },
                    new ReportColumnChoice { Key = "workdays" }
                },
                sortKey: "pieces"));

            Assert.DoesNotContain(table.Columns, c => c.Key == "pieces");
            Assert.Equal("سعيد", table.Rows[0].Label);
        }

        [Fact]
        public async Task ALayoutPointingAtColumnsThatNoLongerExist_IsIgnoredNotFatal()
        {
            // قالب قديم لموضوع أعمدته اتغيّرت — يفضل يفتح
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(ProductionSpec(
                columns: new[] { new ReportColumnChoice { Key = "عمود_مش_موجود" } },
                sortKey: "كمان_مش_موجود"));

            Assert.NotEmpty(table.Columns);
            Assert.NotEmpty(table.Rows);
        }

        [Fact]
        public void EveryColumnInEveryReport_HasAKey_AndNoSubjectRepeatsOne()
        {
            // المفاتيح هي اللي القوالب بتشاور بيها؛ مفتاح مكرر في نفس
            // الموضوع معناه الإخفاء هيضرب العمود الغلط
            foreach (var subject in Enum.GetValues<ReportSubject>())
                foreach (var grouping in Enum.GetValues<ReportGrouping>())
                {
                    var columns = ReportBuilderService.ColumnsFor(subject, grouping);

                    Assert.All(columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Key)));
                    Assert.Equal(columns.Count, columns.Select(c => c.Key).Distinct().Count());
                }
        }

        // ======================= التجميع بأكتر من مستوى =======================

        private static ReportSpec Levels(ReportGrouping groupBy, params ReportGrouping[] thenBy) => new()
        {
            Subject = ReportSubject.Production,
            GroupBy = groupBy,
            ThenBy = thenBy,
            From = Day,
            To = Day
        };

        [Fact]
        public async Task GroupingByWorkerThenStage_GivesOneRowPerStage_NotOnePerWorker()
        {
            // أحمد اشتغل مرحلتين — عايزينه سطرين، كل رقم منسوب لمرحلته
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 60);

            var table = await BuildAsync(Levels(ReportGrouping.Worker, ReportGrouping.Stage));

            Assert.Equal(2, table.Rows.Count);
            Assert.All(table.Rows, r => Assert.Equal("أحمد", r.Label));

            var stageColumn = table.Columns.FindIndex(c => c.Key == "dim_stage");
            Assert.True(stageColumn >= 0, "لازم يبقى فيه عمود للمرحلة");

            var stages = table.Rows.Select(r => r.Texts[stageColumn]).ToList();
            Assert.Contains(stages, s => s!.Contains("قص"));
            Assert.Contains(stages, s => s!.Contains("خياطة"));
        }

        [Fact]
        public async Task TheExtraLevelIsARealColumn_SoTheColumnEditorCanHideIt()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                ThenBy = new[] { ReportGrouping.Stage },
                From = Day,
                To = Day,
                ColumnLayout = new[]
                {
                    new ReportColumnChoice { Key = "dim_stage", Visible = false },
                    new ReportColumnChoice { Key = "pieces" }
                }
            });

            Assert.DoesNotContain(table.Columns, c => c.Key == "dim_stage");
            Assert.Equal(100, table.Rows[0].Values[table.Columns.FindIndex(c => c.Key == "pieces")]);
        }

        [Fact]
        public async Task ProductThenWorkerThenStage_AnswersWhoWorkedOnThisProduct()
        {
            // الطلب بالحرف: مين اشتغل على المنتج ده، بمرحلته وأيامه ويومياته
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 60);

            var table = await BuildAsync(
                Levels(ReportGrouping.Product, ReportGrouping.Worker, ReportGrouping.Stage));

            Assert.Equal(2, table.Rows.Count);
            Assert.All(table.Rows, r => Assert.Equal("شنطة", r.Label));

            var worker = table.Columns.FindIndex(c => c.Key == "dim_worker");
            var stage = table.Columns.FindIndex(c => c.Key == "dim_stage");
            var days = table.Columns.FindIndex(c => c.Key == "workdays_with_work");

            Assert.Contains(table.Rows, r => r.Texts[worker] == "أحمد");
            Assert.Contains(table.Rows, r => r.Texts[stage]!.Contains("خياطة"));
            Assert.All(table.Rows, r => Assert.Equal(1, r.Values[days]));
        }

        [Fact]
        public async Task WithAWorkerOrStageLevel_PiecesAreTheStagesOwnPieces_NotCompletedOutput()
        {
            // القطعة عدّت على مرحلتين مش آخر مرحلة. لو حسبناها بالتام
            // السطرين هيطلعوا أصفار والتقرير مالوش لازمة.
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage2Id, 60);

            var table = await BuildAsync(Levels(ReportGrouping.Product, ReportGrouping.Stage));
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");

            Assert.Equal(new decimal?[] { 100, 60 }, table.Rows.Select(r => r.Values[pieces]).OrderByDescending(v => v));

            // ومجموعهم أكبر من التام عن قصد — ده شغل مبذول مش إنتاج تام
            Assert.Equal(160, table.Totals!.Values[pieces]);
        }

        // ======================= الإنتاج الفعلي منفصل عن قطع العمال =======================

        /// <summary>
        /// نطاق واحد بمجموع عامل مختلف عمدًا عن رقم النطاق — عشان نفرّق
        /// بين "بالمنتج" (بيقرا الإنتاج الفعلي) و"بالعامل"/"بالمرحلة"
        /// (بيقرا قطعة العامل نفسها).
        /// </summary>
        private async Task RecordFlowWithMismatchAsync(
            int stageId, int rangePieces, int workerPieces,
            int workerId = TestDatabase.WorkerAhmedId, DateTime? date = null)
        {
            var range = new FlowRangeDto { FromStageId = stageId, ToStageId = stageId, PieceCount = rangePieces };
            var shares = new List<FlowShareDto>
            {
                new() { ProductionStageId = stageId, WorkerId = workerId, PieceCount = workerPieces }
            };

            using var scope = _db.CreateScope();
            await _db.GetService<ProductionFlowService>(scope).RecordFlowAsync(
                TestDatabase.ProductBagId, date ?? Day, new[] { range }, shares, confirmOverride: true);
        }

        [Fact]
        public async Task Grouping_by_product_reads_the_actual_output_number_not_the_workers_sum()
        {
            // آخر مرحلة (تشطيب) عشان "بالمنتج" تعدّها تام. العامل عمل 130
            // ضربة، بس الإنتاج الفعلي المسجَّل للنطاق 100 — رقمين منفصلين.
            await RecordFlowWithMismatchAsync(TestDatabase.BagStage3Id, rangePieces: 100, workerPieces: 130);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Product));
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");

            var row = Assert.Single(table.Rows);
            Assert.Equal(100, row.Values[pieces]);
        }

        [Fact]
        public async Task Grouping_by_worker_reads_the_workers_own_sum_not_the_actual_output_number()
        {
            await RecordFlowWithMismatchAsync(TestDatabase.BagStage3Id, rangePieces: 100, workerPieces: 130);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");

            var row = Assert.Single(table.Rows);
            Assert.Equal(130, row.Values[pieces]);
        }

        [Fact]
        public async Task Grouping_by_product_withAWorkerFilter_readsThatWorkersOwnSum_notTheActualOutputNumber()
        {
            // فلتر بعامل معيّن معناه السؤال بقى "هو عمل قد إيه" — نفس
            // قاعدة التجميع بالعامل بالظبط، حتى لو التجميع الظاهر
            // نفسه بالمنتج (ProductionOutputRecordDto أصلاً مالوش
            // WorkerId يتفلتر بيه، فمينفعش رقم "التام" يتقصّ على عامل)
            await RecordFlowWithMismatchAsync(
                TestDatabase.BagStage3Id, rangePieces: 100, workerPieces: 130, workerId: TestDatabase.WorkerAhmedId);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Product,
                From = Day,
                To = Day,
                WorkerIds = new[] { TestDatabase.WorkerAhmedId }
            });
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");

            var row = Assert.Single(table.Rows);
            Assert.Equal(130, row.Values[pieces]); // ضربات أحمد هو، مش الإنتاج الفعلي المسجَّل (100)
        }

        [Fact]
        public async Task Grouping_by_product_withAWorkerFilter_onlySumsTheFilteredWorkersRows()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 300);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage3Id, 200);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Product,
                From = Day,
                To = Day,
                WorkerIds = new[] { TestDatabase.WorkerAhmedId }
            });
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");

            var row = Assert.Single(table.Rows);
            Assert.Equal(300, row.Values[pieces]); // أحمد بس — مش الـ500 (مجموع أحمد وسعيد)
        }

        [Fact]
        public async Task WithNoExtraLevels_TheReportIsExactlyAsItWasBefore()
        {
            // التجميع بمستوى واحد لازم يفضل زي ما هو بالحرف
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage3Id, 100);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Product));

            Assert.DoesNotContain(table.Columns, c => c.Key.StartsWith("dim_"));
            Assert.Equal(100, table.Rows[0].Values[0]);
        }

        [Fact]
        public async Task RepeatingTheMainLevelInThenBy_IsIgnored_NotDuplicated()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);

            var table = await BuildAsync(Levels(ReportGrouping.Worker, ReportGrouping.Worker));

            Assert.DoesNotContain(table.Columns, c => c.Key == "dim_worker");
            Assert.Single(table.Rows);
        }

        [Fact]
        public async Task TheBuiltInTemplate_WhoWorkedOnThisProduct_ProducesTheAskedForTable()
        {
            // الطلب: "تقرير لمنتج، اسم الناس اللي اشتغلت عليه بمرحلتهم
            // وعدد أيامهم ويومياتهم" — بنتأكد إن القالب الجاهز بيطلّعه
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 100);
            await RecordAsync(TestDatabase.WorkerSaidId, TestDatabase.BagStage2Id, 60);

            var template = ReportTemplateStore.BuiltIn()
                .Single(t => t.Name == "مين اشتغل على المنتج");

            var spec = template.ToSpec();
            var table = await BuildAsync(new ReportSpec
            {
                Subject = spec.Subject,
                GroupBy = spec.GroupBy,
                ThenBy = spec.ThenBy,
                From = Day,
                To = Day
            });

            // المنتج، العامل، المرحلة، الأيام، اليوميات — كلهم موجودين
            Assert.Equal("المنتج", table.LabelHeader);
            Assert.Contains(table.Columns, c => c.Key == "dim_worker");
            Assert.Contains(table.Columns, c => c.Key == "dim_stage");
            Assert.Contains(table.Columns, c => c.Key == "workdays");
            Assert.Contains(table.Columns, c => c.Key == "workdays_with_work");

            Assert.Equal(2, table.Rows.Count);
            Assert.All(table.Rows, r => Assert.Equal("شنطة", r.Label));
        }

        // ======================= المقارنة بالمدة السابقة =======================

        [Fact]
        public async Task ComparingWithThePreviousPeriod_AddsThePreviousNumberAndThePercentChange()
        {
            // المدة المختارة يوم واحد، فالسابقة هي اليوم اللي قبله
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 50, Day.AddDays(-1));
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 75, Day);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day,
                CompareWithPrevious = true
            });

            var row = Assert.Single(table.Rows);
            var pieces = table.Columns.FindIndex(c => c.Key == "pieces");
            var before = table.Columns.FindIndex(c => c.Key == "pieces__prev");
            var change = table.Columns.FindIndex(c => c.Key == "pieces__delta");

            Assert.Equal(75, row.Values[pieces]);
            Assert.Equal(50, row.Values[before]);
            Assert.Equal(50m, row.Values[change]); // من 50 لـ75 = +50%
        }

        [Fact]
        public async Task AGroupThatDidNotExistBefore_GetsAnEmptyPrevious_NotAZero()
        {
            // الفرق مهم: صفر معناه "اشتغل وطلّع صفر"، والفاضي معناه
            // "مكانش في الصورة" — ونسبة التغير من صفر مالهاش معنى
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 40, Day);

            var table = await BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Production,
                GroupBy = ReportGrouping.Worker,
                From = Day,
                To = Day,
                CompareWithPrevious = true
            });

            var row = Assert.Single(table.Rows);
            var before = table.Columns.FindIndex(c => c.Key == "pieces__prev");
            var change = table.Columns.FindIndex(c => c.Key == "pieces__delta");

            Assert.Null(row.Values[before]);
            Assert.Null(row.Values[change]);
        }

        [Fact]
        public async Task WithoutAskingForIt_NoComparisonColumnsShowUp()
        {
            await RecordAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id, 40);

            var table = await BuildAsync(Spec(ReportSubject.Production, ReportGrouping.Worker));

            Assert.DoesNotContain(table.Columns, c => c.Key.EndsWith("__prev"));
        }

        // ======================= التركيبات المسموحة =======================

        [Fact]
        public void Attendance_cannot_be_grouped_by_product_because_that_has_no_meaning()
        {
            Assert.False(ReportSpec.IsAllowed(ReportSubject.Attendance, ReportGrouping.Product));
            Assert.True(ReportSpec.IsAllowed(ReportSubject.Attendance, ReportGrouping.Worker));
        }

        [Fact]
        public void Production_can_be_cut_by_every_dimension_a_production_record_has()
        {
            // سجل الإنتاج عنده عامل ومرحلة (ومنها المنتج) وتاريخ —
            // فالأربعة دول وأسبوعهم كلهم ينفعوا
            foreach (var grouping in new[]
                     {
                         ReportGrouping.Worker, ReportGrouping.Product,
                         ReportGrouping.Stage, ReportGrouping.Day, ReportGrouping.Week,
                         ReportGrouping.Month
                     })
                Assert.True(ReportSpec.IsAllowed(ReportSubject.Production, grouping));

            // "سبب" بُعد بتاع الهالك لوحده — سجل الإنتاج مالوش سبب
            Assert.False(ReportSpec.IsAllowed(ReportSubject.Production, ReportGrouping.Reason));
            Assert.True(ReportSpec.IsAllowed(ReportSubject.Scrap, ReportGrouping.Reason));
            Assert.True(ReportSpec.IsAllowed(ReportSubject.Scrap, ReportGrouping.Month));
        }

        [Fact]
        public void Scrap_cannot_be_cut_by_worker_because_it_has_none()
        {
            // القطعة عدّت على مراحل كتير قبل ما تتشال، فنسبها لعامل
            // واحد قرار إداري مش حقيقة في البيانات
            Assert.False(ReportSpec.IsAllowed(ReportSubject.Scrap, ReportGrouping.Worker));
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
