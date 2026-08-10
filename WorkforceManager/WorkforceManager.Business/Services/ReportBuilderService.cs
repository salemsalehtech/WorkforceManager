using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيحوّل <see cref="ReportSpec"/> لجدول جاهز للعرض والتصدير.
    ///
    /// **مفيش أي قاعدة حسابية جديدة هنا.** الخدمة دي بتجمّع وتشكّل بس:
    /// الأجور بتتحسب في <see cref="PayrollService"/>، واليوميات في
    /// <see cref="WorkdayMath"/>، وخصم الغياب في
    /// <see cref="AbsenceDeductionRule"/>، والأسبوع في
    /// <see cref="WeeklySummaryService"/>. لو أي رقم هنا اتحسب من
    /// جديد، هيبقى فيه شاشتين بيقولوا رقمين لنفس الشغل — وده بالظبط
    /// اللي البرنامج بيتجنّبه في كل حتة.
    ///
    /// النتيجة دايمًا <see cref="ReportTable"/>: جدول عام. يعني
    /// مُصدِّر Excel واحد وشبكة عرض واحدة بيخدموا كل المواضيع
    /// وتجميعاتها.
    /// </summary>
    public class ReportBuilderService
    {
        private readonly IDailyProductionRepository _production;
        private readonly IAttendanceRepository _attendance;
        private readonly IPenaltyRepository _penalties;
        private readonly IWageAdjustmentRepository _adjustments;
        private readonly IHourlyWorkLogRepository _hourly;
        private readonly IWorkerRepository _workers;
        private readonly IProductRepository _products;
        private readonly PayrollService _payroll;

        public ReportBuilderService(
            IDailyProductionRepository production,
            IAttendanceRepository attendance,
            IPenaltyRepository penalties,
            IWageAdjustmentRepository adjustments,
            IHourlyWorkLogRepository hourly,
            IWorkerRepository workers,
            IProductRepository products,
            PayrollService payroll)
        {
            _production = production;
            _attendance = attendance;
            _penalties = penalties;
            _adjustments = adjustments;
            _hourly = hourly;
            _workers = workers;
            _products = products;
            _payroll = payroll;
        }

        public async Task<ReportTable> BuildAsync(ReportSpec spec)
        {
            var table = await BuildTableAsync(spec);

            if (spec.CompareWithPrevious && ReportSpec.UsesPeriod(spec.Subject))
                await AddPreviousPeriodAsync(table, spec);

            // الترتيب قبل التخطيط عشان ينفع بعمود مخفي، والاتنين قبل
            // الإجماليات عشان الإجمالي يطلع على الأعمدة والصفوف
            // المعروضة فعلاً
            return table
                .ApplySort(spec.SortKey, spec.SortDescending, spec.TopN)
                .ApplyLayout(spec.ColumnLayout)
                .WithTotals();
        }

        /// <summary>
        /// بيزوّد لكل عمود رقمي عمودين: نفس الرقم في المدة اللي قبلها،
        /// ونسبة الفرق.
        ///
        /// **الصفوف بتتطابق بالاسم.** العامل اللي اشتغل الشهر ده ومكانش
        /// موجود الشهر اللي فات بياخد فاضي في عمود "السابق" مش صفر —
        /// الفرق بينهم إن الصفر بيقول "اشتغل وطلّع صفر" والفاضي بيقول
        /// "مكانش في الصورة أصلاً"، ونسبة التغير من صفر مالهاش معنى.
        ///
        /// المدة السابقة بنفس الطول بالظبط (<see cref="ReportSpec.PreviousPeriod"/>)
        /// عشان المقارنة تفضل عادلة مهما كانت المدة المختارة، ومن غير
        /// أي فلتر يتغيّر — نفس العمال ونفس المنتجات.
        /// </summary>
        private async Task AddPreviousPeriodAsync(ReportTable table, ReportSpec spec)
        {
            var (from, to) = spec.PreviousPeriod();

            var previous = await BuildTableAsync(new ReportSpec
            {
                Subject = spec.Subject,
                GroupBy = spec.GroupBy,
                From = from,
                To = to,
                WorkerIds = spec.WorkerIds,
                ProductIds = spec.ProductIds,
                StageIds = spec.StageIds,
                WorkerKind = spec.WorkerKind
                // من غير ColumnLayout/Sort/TopN: دي شكل العرض، والمقارنة
                // محتاجة الأرقام الخام بترتيب الأعمدة الأصلي
            });

            var previousByLabel = previous.Rows
                .GroupBy(r => r.Label)
                .ToDictionary(g => g.Key, g => g.First());

            var numeric = table.Columns
                .Select((column, index) => (column, index))
                .Where(x => x.column.Kind != ReportValueKind.Text)
                .ToList();

            var additions = new List<ReportColumn>();

            foreach (var (column, _) in numeric)
            {
                additions.Add(new ReportColumn
                {
                    Key = column.Key + PreviousSuffix,
                    Header = column.Header + " (السابق)",
                    Kind = column.Kind,
                    Sums = column.Sums
                });

                additions.Add(new ReportColumn
                {
                    Key = column.Key + ChangeSuffix,
                    Header = column.Header + " (التغير %)",
                    Kind = ReportValueKind.Fraction
                    // مجموع النِسَب رقم مالوش معنى — مبيتجمعش
                });
            }

            foreach (var row in table.Rows)
            {
                previousByLabel.TryGetValue(row.Label, out var before);

                foreach (var (_, index) in numeric)
                {
                    var now = index < row.Values.Count ? row.Values[index] : null;
                    var then = before is not null && index < before.Values.Count
                        ? before.Values[index]
                        : null;

                    row.Values.Add(then);
                    row.Values.Add(PercentChange(now, then));
                }
            }

            table.Columns.AddRange(additions);
        }

        private const string PreviousSuffix = "__prev";
        private const string ChangeSuffix = "__delta";

        /// <summary>
        /// نسبة التغير. بترجع فاضية لو المدة السابقة مفيهاش رقم أصلاً
        /// أو كانت صفر — القسمة على صفر مالهاش ناتج، و"زيادة ∞%" مش رقم
        /// المدير يقدر يقرا منه حاجة.
        /// </summary>
        private static decimal? PercentChange(decimal? now, decimal? then)
        {
            if (then is null or 0 || now is null) return null;

            return Math.Round((now.Value - then.Value) / Math.Abs(then.Value) * 100m, 1);
        }

        /// <summary>
        /// أعمدة الموضوع بترتيبها الافتراضي — من غير أي بيانات.
        ///
        /// الشاشة محتاجة تعرف الأعمدة المتاحة عشان تبني محرّر الأعمدة
        /// **قبل** ما المستخدم يطلع التقرير، ومن غير ما تستنى استعلام.
        /// المصدر هنا هو نفس الجداول اللي البناء بيستخدمها، فمفيش
        /// قايمة تانية تتنسى تتحدّث لما عمود يتزوّد.
        /// </summary>
        public static IReadOnlyList<ReportColumn> ColumnsFor(ReportSubject subject, ReportGrouping grouping) =>
            subject switch
            {
                ReportSubject.Production => new[]
                {
                    new ReportColumn { Header = "القطع", Key = "pieces", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "اليوميات", Key = "workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "عدد العمال", Key = "workers", Kind = ReportValueKind.Whole },
                    new ReportColumn { Header = "أيام فيها شغل", Key = "workdays_with_work", Kind = ReportValueKind.Whole }
                },

                ReportSubject.Attendance => new[]
                {
                    new ReportColumn { Header = "حضور", Key = "present", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "غياب بإذن", Key = "absent_excused", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "غياب بدون إذن", Key = "absent_unexcused", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "خصم الغياب (يومية)", Key = "absence_deduction", Kind = ReportValueKind.Fraction, Sums = true }
                },

                ReportSubject.Wages => new[]
                {
                    new ReportColumn { Header = "يوميات منتجة", Key = "produced_workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "خصم غياب", Key = "absence_deduction", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "خصم جزاءات", Key = "penalty_deduction", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "صافي اليوميات", Key = "net_workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "حوافز", Key = "bonus", Kind = ReportValueKind.Money, Sums = true },
                    new ReportColumn { Header = "سلف", Key = "advance", Kind = ReportValueKind.Money, Sums = true },
                    new ReportColumn { Header = "الأجر النهائي", Key = "net_wage", Kind = ReportValueKind.Money, Sums = true }
                },

                ReportSubject.Penalties => new[]
                {
                    new ReportColumn { Header = "عدد الجزاءات", Key = "penalty_count", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "إجمالي الخصم (يومية)", Key = "penalty_total", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "منها تلقائي (غياب)", Key = "penalty_auto", Kind = ReportValueKind.Whole, Sums = true }
                },

                ReportSubject.WageAdjustments => new[]
                {
                    new ReportColumn { Header = "سلف", Key = "advance", Kind = ReportValueKind.Money, Sums = true },
                    new ReportColumn { Header = "حوافز", Key = "bonus", Kind = ReportValueKind.Money, Sums = true },
                    new ReportColumn { Header = "الصافي", Key = "net", Kind = ReportValueKind.Money, Sums = true }
                },

                // المهارات بتغيّر معنى عمودين حسب التجميع: بالعامل
                // السؤال "بيعرف كام مرحلة"، وبالمنتج/المرحلة السؤال
                // المعكوس "المرحلة دي عندها كام عامل" — وده اللي بيمنع
                // مرحلة تقف من غير ما حد ياخد باله
                ReportSubject.Skills => grouping == ReportGrouping.Worker
                    ? new[]
                    {
                        new ReportColumn { Header = "عدد المهارات", Key = "skill_count", Kind = ReportValueKind.Whole, Sums = true },
                        new ReportColumn { Header = "متوسط النجوم", Key = "avg_stars", Kind = ReportValueKind.Fraction },
                        new ReportColumn { Header = "منتجات بيعرفها", Key = "known_products", Kind = ReportValueKind.Whole }
                    }
                    : new[]
                    {
                        new ReportColumn { Header = "عمال مؤهلين", Key = "qualified_workers", Kind = ReportValueKind.Whole, Sums = true },
                        new ReportColumn { Header = "متوسط النجوم", Key = "avg_stars", Kind = ReportValueKind.Fraction },
                        new ReportColumn { Header = "عدد المراحل", Key = "stage_count", Kind = ReportValueKind.Whole, Sums = true }
                    },

                _ => Array.Empty<ReportColumn>()
            };

        public IReadOnlyList<ReportColumn> AvailableColumns(ReportSubject subject, ReportGrouping grouping) =>
            ColumnsFor(subject, grouping);

        /// <summary>
        /// السجلات الخام ورا التقرير — سطر لكل سجل من غير أي تجميع.
        ///
        /// بيتصدّر في شيت لوحده عشان المستخدم يعمل Pivot بنفسه بدل ما
        /// يستنى عمود جديد يتكتب في البرنامج. الفلاتر هي **نفسها**
        /// المستخدمة في الملخص عشان الشيتين يتكلموا عن نفس الشغل —
        /// شيت تفاصيل بيقول غير الملخص أسوأ من إنه مش موجود.
        ///
        /// الترتيب والتخطيط وأعلى/أقل N مش بتتطبّق هنا عن قصد: دي شكل
        /// عرض الملخص، والتفاصيل غرضها تبقى خام وكاملة.
        /// </summary>
        public async Task<ReportDetail> BuildDetailAsync(ReportSpec spec)
        {
            var names = await WorkerNamesAsync();

            return spec.Subject switch
            {
                ReportSubject.Production => await ProductionDetailAsync(spec),
                ReportSubject.Attendance => await AttendanceDetailAsync(spec, names),
                ReportSubject.Penalties => await PenaltiesDetailAsync(spec, names),
                ReportSubject.WageAdjustments => await AdjustmentsDetailAsync(spec, names),
                ReportSubject.Skills => await SkillsDetailAsync(spec),

                // الأجور محسوبة لكل عامل عن المدة كلها — مفيش "سجل خام"
                // تحتها غير الملخص نفسه، فمفيش شيت تفاصيل ليها
                _ => new ReportDetail { SheetName = "تفاصيل" }
            };
        }

        private static ReportColumn Text(string header, string key) =>
            new() { Header = header, Key = key, Kind = ReportValueKind.Text };

        private async Task<ReportDetail> ProductionDetailAsync(ReportSpec spec)
        {
            var rows = Filter(await _production.GetByRangeAsync(spec.From, spec.To), spec);

            var detail = new ReportDetail
            {
                SheetName = "سجلات الإنتاج",
                Columns =
                {
                    Text("اليوم", "date"),
                    Text("العامل", "worker"),
                    Text("المنتج", "product"),
                    Text("المرحلة", "stage"),
                    new ReportColumn { Header = "القطع", Key = "pieces", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "اليوميات", Key = "workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "الكوتة وقت التسجيل", Key = "quota", Kind = ReportValueKind.Whole }
                }
            };

            foreach (var r in rows.OrderBy(r => r.Date).ThenBy(r => r.Worker?.FullName))
                detail.Rows.Add(new ReportDetailRow
                {
                    GroupLabel = Key(r, spec.GroupBy).Label,
                    Cells =
                    {
                        ReportCell.Of(r.Date),
                        ReportCell.Of(r.Worker?.FullName),
                        ReportCell.Of(r.ProductionStage?.Product?.Name),
                        ReportCell.Of(r.ProductionStage?.StageName),
                        ReportCell.Of(r.PieceCount),
                        ReportCell.Of(r.WorkdaysCompleted),
                        ReportCell.Of(r.PiecesPerWorkdayAtEntry)
                    }
                });

            return detail;
        }

        private async Task<ReportDetail> AttendanceDetailAsync(
            ReportSpec spec, IReadOnlyDictionary<int, string> names)
        {
            var allowed = await AllowedWorkerIdsAsync(spec);
            var rows = (await _attendance.GetByRangeAsync(spec.From, spec.To))
                .Where(a => allowed is null || allowed.Contains(a.WorkerId))
                .ToList();

            var detail = new ReportDetail
            {
                SheetName = "سجلات الحضور",
                Columns = { Text("اليوم", "date"), Text("العامل", "worker"), Text("الحالة", "status") }
            };

            foreach (var a in rows.OrderBy(a => a.Date).ThenBy(a => names.GetValueOrDefault(a.WorkerId)))
                detail.Rows.Add(new ReportDetailRow
                {
                    GroupLabel = spec.GroupBy == ReportGrouping.Worker
                        ? names.GetValueOrDefault(a.WorkerId, "—")
                        : DayKey(a.Date).Label,
                    Cells =
                    {
                        ReportCell.Of(a.Date),
                        ReportCell.Of(names.GetValueOrDefault(a.WorkerId, "—")),
                        ReportCell.Of(a.Status.ToArabicName())
                    }
                });

            return detail;
        }

        private async Task<ReportDetail> PenaltiesDetailAsync(
            ReportSpec spec, IReadOnlyDictionary<int, string> names)
        {
            var allowed = await AllowedWorkerIdsAsync(spec);
            var rows = (await _penalties.GetByRangeAsync(spec.From, spec.To))
                .Where(p => allowed is null || allowed.Contains(p.WorkerId))
                .ToList();

            var detail = new ReportDetail
            {
                SheetName = "سجلات الجزاءات",
                Columns =
                {
                    Text("اليوم", "date"),
                    Text("العامل", "worker"),
                    Text("السبب", "reason"),
                    new ReportColumn { Header = "الخصم (يومية)", Key = "deduction", Kind = ReportValueKind.Fraction, Sums = true },
                    Text("المصدر", "source")
                }
            };

            foreach (var p in rows.OrderBy(p => p.Date).ThenBy(p => names.GetValueOrDefault(p.WorkerId)))
                detail.Rows.Add(new ReportDetailRow
                {
                    GroupLabel = spec.GroupBy == ReportGrouping.Worker
                        ? names.GetValueOrDefault(p.WorkerId, "—")
                        : DayKey(p.Date).Label,
                    Cells =
                    {
                        ReportCell.Of(p.Date),
                        ReportCell.Of(names.GetValueOrDefault(p.WorkerId, "—")),
                        ReportCell.Of(p.Reason),
                        ReportCell.Of(p.DeductedWorkdays),
                        ReportCell.Of(p.IsAutoGenerated ? "تلقائي (غياب)" : "يدوي")
                    }
                });

            return detail;
        }

        private async Task<ReportDetail> AdjustmentsDetailAsync(
            ReportSpec spec, IReadOnlyDictionary<int, string> names)
        {
            var allowed = await AllowedWorkerIdsAsync(spec);
            var rows = (await _adjustments.GetByRangeAsync(spec.From, spec.To))
                .Where(a => allowed is null || allowed.Contains(a.WorkerId))
                .ToList();

            var detail = new ReportDetail
            {
                SheetName = "سجلات السلف والحوافز",
                Columns =
                {
                    Text("اليوم", "date"),
                    Text("العامل", "worker"),
                    Text("النوع", "type"),
                    new ReportColumn { Header = "المبلغ", Key = "amount", Kind = ReportValueKind.Money, Sums = true },
                    Text("ملاحظة", "note")
                }
            };

            foreach (var a in rows.OrderBy(a => a.Date).ThenBy(a => names.GetValueOrDefault(a.WorkerId)))
                detail.Rows.Add(new ReportDetailRow
                {
                    GroupLabel = spec.GroupBy == ReportGrouping.Worker
                        ? names.GetValueOrDefault(a.WorkerId, "—")
                        : DayKey(a.Date).Label,
                    Cells =
                    {
                        ReportCell.Of(a.Date),
                        ReportCell.Of(names.GetValueOrDefault(a.WorkerId, "—")),
                        ReportCell.Of(a.Type == WageAdjustmentType.Bonus ? "حافز" : "سلفة"),
                        ReportCell.Of(a.AmountEgp),
                        ReportCell.Of(a.Note)
                    }
                });

            return detail;
        }

        private async Task<ReportDetail> SkillsDetailAsync(ReportSpec spec)
        {
            var workers = (await _workers.GetAllWithSkillsAsync())
                .Where(w => Matches(w, spec))
                .ToList();

            var detail = new ReportDetail
            {
                SheetName = "سجلات المهارات",
                Columns =
                {
                    Text("العامل", "worker"),
                    Text("المنتج", "product"),
                    Text("المرحلة", "stage"),
                    new ReportColumn { Header = "النجوم", Key = "stars", Kind = ReportValueKind.Whole }
                }
            };

            foreach (var w in workers.OrderBy(w => w.FullName))
                foreach (var s in w.Skills.Where(s => s.ProductionStage?.Product is not null))
                {
                    var product = s.ProductionStage.Product;

                    if (spec.ProductIds is { Count: > 0 } && !spec.ProductIds.Contains(product.Id)) continue;
                    if (spec.StageIds is { Count: > 0 } && !spec.StageIds.Contains(s.ProductionStageId)) continue;

                    detail.Rows.Add(new ReportDetailRow
                    {
                        GroupLabel = spec.GroupBy == ReportGrouping.Worker ? w.FullName : product.Name,
                        Cells =
                        {
                            ReportCell.Of(w.FullName),
                            ReportCell.Of(product.Name),
                            ReportCell.Of(s.ProductionStage.StageName),
                            ReportCell.Of(s.Stars)
                        }
                    });
                }

            return detail;
        }

        private async Task<ReportTable> BuildTableAsync(ReportSpec spec) => spec.Subject switch
        {
            ReportSubject.Production => await ProductionAsync(spec),
            ReportSubject.Attendance => await AttendanceAsync(spec),
            ReportSubject.Wages => await WagesAsync(spec),
            ReportSubject.Penalties => await PenaltiesAsync(spec),
            ReportSubject.WageAdjustments => await AdjustmentsAsync(spec),
            ReportSubject.Skills => await SkillsAsync(spec),
            _ => throw new InvalidOperationException("موضوع تقرير غير معروف")
        };

        // ======================= الإنتاج =======================

        /// <summary>
        /// عمود "القطع" معناه بيتغيّر حسب اللي بنجمّع عليه:
        ///
        ///   • بالعامل أو بالمرحلة → المجموعة نفسها **وحدة شغل**. مجموع
        ///     سجلاتها هو شغلها بالظبط، وكل سجل شغل حقيقي اتعمل بإيد
        ///     ومستحق عليه يومية. الجمع هنا صح.
        ///
        ///   • بالمنتج أو باليوم أو بالأسبوع → المجموعة **وعاء إنتاج**،
        ///     والسؤال هو "خرج من الخط كام؟". القطعة الواحدة بتعدّي على
        ///     كل المراحل، فجمعها كلها بيعدّها مرة لكل مرحلة — منتج بـ11
        ///     مرحلة كان بيبان إنتاجه 11 ضعف الحقيقي. آخر مرحلة بس هي
        ///     اللي بتتعدّ، وهي نفس القاعدة اللي الرسم البياني وتقرير
        ///     اليوم والتقرير العام شغالين بيها (<see cref="ProductionLine"/>)
        ///     — فالبرنامج كله بيقول نفس الرقم.
        /// </summary>
        /// <summary>
        /// المجموعة بتتقاس بالتام ولا بالشغل المبذول؟
        ///
        /// **أي مستوى بالعامل أو بالمرحلة بيحوّل المجموعة لوحدة شغل.**
        /// "المنتج ده مين اشتغل عليه وفي أنهي مرحلة" سؤاله لكل سطر هو
        /// "العامل ده عمل قد إيه في المرحلة دي" — والإجابة قطع المرحلة
        /// نفسها. لو حسبناها بالتام (آخر مرحلة بس) كل السطور اللي مش
        /// على آخر مرحلة هتطلع أصفار، والتقرير يبقى مالوش لازمة.
        ///
        /// ومجموع السطور ساعتها بيطلع أكبر من إنتاج المنتج التام، وده
        /// صح مش غلط: القطعة عدّت على كذا مرحلة واشتغل فيها كذا عامل.
        /// </summary>
        private static bool CountsCompletedOutput(IReadOnlyList<ReportGrouping> levels)
        {
            if (levels.Any(l => l is ReportGrouping.Worker or ReportGrouping.Stage))
                return false;

            return levels[0] is ReportGrouping.Product or ReportGrouping.Day or ReportGrouping.Week;
        }

        private async Task<ReportTable> ProductionAsync(ReportSpec spec)
        {
            var rows = Filter(await _production.GetByRangeAsync(spec.From, spec.To), spec);
            var levels = spec.AllLevels();

            // "اليوميات" و"عدد العمال" و"أيام فيها شغل" بيتجمّعوا على كل
            // المراحل دايمًا: دول بيقيسوا **الشغل المبذول**، والقطعة اللي
            // عدّت على 11 مرحلة اشتغل فيها 11 عامل وكل واحد ليه يوميته.
            //
            // "القطع" مختلفة، وبتتقاس حسب معنى المجموعة نفسها —
            // <see cref="CountsCompletedOutput"/>.
            var lastStageIds = CountsCompletedOutput(levels)
                ? ProductionLine
                    .LastStageIdByProduct(await _products.GetAllWithStagesAsync())
                    .Values.ToHashSet()
                : null;

            // مستويات التجميع الإضافية بتبقى أعمدة نصية قبل الأرقام،
            // فمحرّر الأعمدة يقدر يخفيها أو يرتّبها زي أي عمود
            var levelColumns = levels.Skip(1)
                .Select(level => Text(ReportSpec.GroupingLabel(level), LevelKey(level)))
                .ToList();

            var table = NewTable(spec, levelColumns.Concat(ColumnsFor(spec.Subject, spec.GroupBy)));

            var groups = rows
                .GroupBy(r => new CompositeKey(levels.Select(level => Key(r, level).Label).ToList()))
                .OrderBy(g => g.Key.Parts[0])
                .ThenBy(g => g.Key.Rest)
                .ToList();

            foreach (var g in groups)
            {
                var row = new ReportRow { Label = g.Key.Parts[0] };

                // الأعمدة النصية بتتحط في Texts والأرقام في Values —
                // الاتنين مفهرسين بنفس ترتيب الأعمدة، فلازم كل عمود ياخد
                // مكانه في القايمتين حتى لو فاضي في واحدة منهم
                for (var i = 1; i < g.Key.Parts.Count; i++)
                {
                    row.Values.Add(null);
                    row.Texts.Add(g.Key.Parts[i]);
                }

                foreach (var value in new decimal?[]
                {
                    lastStageIds is null
                        ? g.Sum(r => r.PieceCount)
                        : g.Where(r => lastStageIds.Contains(r.ProductionStageId)).Sum(r => r.PieceCount),
                    g.Sum(r => r.WorkdaysCompleted),
                    g.Select(r => r.WorkerId).Distinct().Count(),
                    g.Select(r => r.Date.Date).Distinct().Count()
                })
                {
                    row.Values.Add(value);
                    row.Texts.Add(null);
                }

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>مفتاح مركّب من كل مستويات التجميع</summary>
        private readonly record struct CompositeKey(List<string> Parts)
        {
            public string Rest => string.Join(" | ", Parts.Skip(1));

            public bool Equals(CompositeKey other) => Parts.SequenceEqual(other.Parts);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (var part in Parts) hash.Add(part);
                return hash.ToHashCode();
            }
        }

        private static string LevelKey(ReportGrouping level) => "dim_" + level.ToString().ToLowerInvariant();

        // ======================= الحضور =======================

        private async Task<ReportTable> AttendanceAsync(ReportSpec spec)
        {
            var all = await _attendance.GetByRangeAsync(spec.From, spec.To);
            var allowed = await AllowedWorkerIdsAsync(spec);

            var rows = all.Where(a => allowed is null || allowed.Contains(a.WorkerId)).ToList();
            var names = await WorkerNamesAsync();

            var groups = rows
                .GroupBy(a => spec.GroupBy switch
                {
                    ReportGrouping.Worker => new GroupKey(names.GetValueOrDefault(a.WorkerId, "—"), 0),
                    ReportGrouping.Week => WeekKey(a.Date),
                    _ => DayKey(a.Date)
                })
                .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Label)
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            foreach (var g in groups)
            {
                var unexcused = g.Count(a => a.Status == AttendanceStatus.AbsentWithoutPermission);

                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    Values =
                    {
                        g.Count(a => a.Status == AttendanceStatus.Present),
                        g.Count(a => a.Status == AttendanceStatus.AbsentWithPermission),
                        unexcused,
                        // نفس معدّل الخصم المعتمد — مش رقم مكتوب هنا
                        unexcused * AbsenceDeductionRule.UnexcusedAbsencePerDay
                    }
                });
            }

            return table;
        }

        // ======================= الأجور =======================

        private async Task<ReportTable> WagesAsync(ReportSpec spec)
        {
            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            var allowed = await AllowedWorkerIdsAsync(spec);

            // بالأسبوع: كل أسبوع بيتحسب لوحده بنفس خدمة الأجور، فالأرقام
            // مطابقة لكشف الأسبوع بالظبط
            var periods = spec.GroupBy == ReportGrouping.Week
                ? WeeksIn(spec.From, spec.To)
                : new List<(DateTime From, DateTime To, string Label)> { (spec.From, spec.To, "") };

            foreach (var period in periods)
            {
                // مصدر الحقيقة الوحيد لحساب الأجر — مفيش نسخة تانية هنا
                var payroll = await _payroll.GetPeriodPayrollAsync(period.From, period.To);

                var workers = payroll.Workers
                    .Where(w => allowed is null || allowed.Contains(w.WorkerId))
                    .ToList();

                if (spec.GroupBy == ReportGrouping.Week)
                {
                    if (workers.Count == 0) continue;

                    table.Rows.Add(new ReportRow
                    {
                        Label = period.Label,
                        Values =
                        {
                            workers.Sum(w => w.ProducedWorkdays),
                            workers.Sum(w => w.AbsenceDeduction),
                            workers.Sum(w => w.PenaltyDeduction),
                            workers.Sum(w => w.NetWorkdays),
                            workers.Sum(w => w.BonusEgp),
                            workers.Sum(w => w.AdvanceEgp),
                            workers.Sum(w => w.NetWageEgp)
                        }
                    });
                    continue;
                }

                foreach (var w in workers.OrderByDescending(w => w.NetWageEgp).ThenBy(w => w.WorkerName))
                    table.Rows.Add(new ReportRow
                    {
                        Label = w.WorkerName,
                        Values =
                        {
                            w.ProducedWorkdays, w.AbsenceDeduction, w.PenaltyDeduction,
                            w.NetWorkdays, w.BonusEgp, w.AdvanceEgp, w.NetWageEgp
                        }
                    });
            }

            return table;
        }

        // ======================= الجزاءات =======================

        private async Task<ReportTable> PenaltiesAsync(ReportSpec spec)
        {
            var all = await _penalties.GetByRangeAsync(spec.From, spec.To);
            var allowed = await AllowedWorkerIdsAsync(spec);
            var names = await WorkerNamesAsync();

            var rows = all.Where(p => allowed is null || allowed.Contains(p.WorkerId)).ToList();

            var groups = rows
                .GroupBy(p => spec.GroupBy switch
                {
                    ReportGrouping.Worker => new GroupKey(names.GetValueOrDefault(p.WorkerId, "—"), 0),
                    ReportGrouping.Week => WeekKey(p.Date),
                    _ => DayKey(p.Date)
                })
                .OrderByDescending(g => g.Sum(p => p.Deduction.ToWorkdays()))
                .ThenBy(g => g.Key.Label)
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            foreach (var g in groups)
                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    Values =
                    {
                        g.Count(),
                        g.Sum(p => p.Deduction.ToWorkdays()),
                        g.Count(p => p.Source == PenaltySource.AutoAbsence)
                    }
                });

            return table;
        }

        // ======================= السلف والحوافز =======================

        private async Task<ReportTable> AdjustmentsAsync(ReportSpec spec)
        {
            var all = await _adjustments.GetByRangeAsync(spec.From, spec.To);
            var allowed = await AllowedWorkerIdsAsync(spec);
            var names = await WorkerNamesAsync();

            var rows = all.Where(a => allowed is null || allowed.Contains(a.WorkerId)).ToList();

            var groups = rows
                .GroupBy(a => spec.GroupBy switch
                {
                    ReportGrouping.Worker => new GroupKey(names.GetValueOrDefault(a.WorkerId, "—"), 0),
                    ReportGrouping.Week => WeekKey(a.Date),
                    _ => DayKey(a.Date)
                })
                .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Label)
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            foreach (var g in groups)
            {
                var advances = g.Where(a => a.Type == WageAdjustmentType.Advance).Sum(a => a.AmountEgp);
                var bonuses = g.Where(a => a.Type == WageAdjustmentType.Bonus).Sum(a => a.AmountEgp);

                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    // الصافي من ناحية العامل: الحوافز ليه والسلف عليه
                    Values = { advances, bonuses, bonuses - advances }
                });
            }

            return table;
        }

        // ======================= المهارات =======================

        private async Task<ReportTable> SkillsAsync(ReportSpec spec)
        {
            var workers = (await _workers.GetAllWithSkillsAsync())
                .Where(w => Matches(w, spec))
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            if (spec.GroupBy == ReportGrouping.Worker)
            {
                foreach (var w in workers
                             .Where(w => w.Skills.Count > 0)
                             .OrderByDescending(w => w.Skills.Count).ThenBy(w => w.FullName))
                    table.Rows.Add(new ReportRow
                    {
                        Label = w.FullName,
                        Values =
                        {
                            w.Skills.Count,
                            Math.Round(w.Skills.Average(s => (decimal)s.Stars), 1),
                            w.Skills.Where(s => s.ProductionStage?.Product is not null)
                                .Select(s => s.ProductionStage.ProductId).Distinct().Count()
                        }
                    });

                return table;
            }

            // بالمنتج أو بالمرحلة: العدّ بيبقى "كام عامل مؤهل" — وده
            // السؤال اللي بيمنع مرحلة تقف من غير ما حد ياخد باله
            var products = await _products.GetAllWithStagesAsync();
            var skills = workers.SelectMany(w => w.Skills).ToList();

            var byStage = skills.ToLookup(s => s.ProductionStageId);

            // الأعمدة هنا جايه صح من ColumnsFor حسب التجميع — مفيش
            // تعديل عليها بعد البناء

            foreach (var product in products.OrderBy(p => p.Name))
            {
                if (spec.ProductIds is { Count: > 0 } && !spec.ProductIds.Contains(product.Id)) continue;

                var line = ProductionLine.Active(product);

                if (spec.GroupBy == ReportGrouping.Product)
                {
                    var all = line.SelectMany(s => byStage[s.Id]).ToList();
                    table.Rows.Add(new ReportRow
                    {
                        Label = product.Name,
                        Values =
                        {
                            all.Select(s => s.WorkerId).Distinct().Count(),
                            all.Count == 0 ? null : Math.Round(all.Average(s => (decimal)s.Stars), 1),
                            line.Count
                        }
                    });
                    continue;
                }

                foreach (var stage in line)
                {
                    if (spec.StageIds is { Count: > 0 } && !spec.StageIds.Contains(stage.Id)) continue;

                    var onStage = byStage[stage.Id].ToList();
                    table.Rows.Add(new ReportRow
                    {
                        Label = $"{product.Name} — {stage.StageName}",
                        Values =
                        {
                            onStage.Count,
                            onStage.Count == 0 ? null : Math.Round(onStage.Average(s => (decimal)s.Stars), 1),
                            1
                        }
                    });
                }
            }

            return table;
        }

        // ======================= أدوات مشتركة =======================

        /// <summary>مفتاح تجميع: النص اللي هيتعرض + ترتيبه</summary>
        private readonly record struct GroupKey(string Label, int Order);

        private static GroupKey DayKey(DateTime date) =>
            new(date.ToString("yyyy/MM/dd"), (int)(date.Date - DateTime.MinValue).TotalDays);

        /// <summary>الأسبوع بيتسمّى بأول يوم فيه — والتعريف من WeeklySummaryService</summary>
        private static GroupKey WeekKey(DateTime date)
        {
            var (start, end) = WeeklySummaryService.GetWorkWeekRange(date);
            return new GroupKey($"{start:yyyy/MM/dd} — {end:MM/dd}",
                (int)(start.Date - DateTime.MinValue).TotalDays);
        }

        private static List<(DateTime From, DateTime To, string Label)> WeeksIn(DateTime from, DateTime to)
        {
            var weeks = new List<(DateTime, DateTime, string)>();
            var (cursor, _) = WeeklySummaryService.GetWorkWeekRange(from);

            while (cursor <= to.Date)
            {
                var (start, end) = WeeklySummaryService.GetWorkWeekRange(cursor);
                weeks.Add((start, end, $"{start:yyyy/MM/dd} — {end:MM/dd}"));
                cursor = end.AddDays(1);
            }

            return weeks;
        }

        private GroupKey Key(DailyProduction row, ReportGrouping grouping) => grouping switch
        {
            ReportGrouping.Worker => new GroupKey(row.Worker?.FullName ?? "—", 0),
            ReportGrouping.Product => new GroupKey(row.ProductionStage?.Product?.Name ?? "—", 0),
            ReportGrouping.Stage => new GroupKey(
                $"{row.ProductionStage?.Product?.Name} — {row.ProductionStage?.StageName}", 0),
            ReportGrouping.Week => WeekKey(row.Date),
            _ => DayKey(row.Date)
        };

        private static List<DailyProduction> Filter(IReadOnlyList<DailyProduction> rows, ReportSpec spec) =>
            rows.Where(r =>
                    (spec.WorkerIds is not { Count: > 0 } || spec.WorkerIds.Contains(r.WorkerId)) &&
                    (spec.StageIds is not { Count: > 0 } || spec.StageIds.Contains(r.ProductionStageId)) &&
                    (spec.ProductIds is not { Count: > 0 } ||
                     spec.ProductIds.Contains(r.ProductionStage?.ProductId ?? 0)))
                .ToList();

        /// <summary>null = مفيش فلتر على العمال (كلهم داخلين)</summary>
        private async Task<HashSet<int>?> AllowedWorkerIdsAsync(ReportSpec spec)
        {
            if (spec.WorkerIds is not { Count: > 0 } && spec.WorkerKind == WorkerKindFilter.All)
                return null;

            var workers = await _workers.GetAllWithSkillsAsync();

            return workers.Where(w => Matches(w, spec)).Select(w => w.Id).ToHashSet();
        }

        private static bool Matches(Worker worker, ReportSpec spec)
        {
            if (spec.WorkerIds is { Count: > 0 } && !spec.WorkerIds.Contains(worker.Id)) return false;

            return spec.WorkerKind switch
            {
                WorkerKindFilter.Hourly => worker.HourlyRole is not null,
                WorkerKindFilter.ByProduction => worker.HourlyRole is null,
                _ => true
            };
        }

        private async Task<Dictionary<int, string>> WorkerNamesAsync() =>
            (await _workers.GetAllWithSkillsAsync())
            .GroupBy(w => w.Id)
            .ToDictionary(g => g.Key, g => g.First().FullName);

        private static ReportTable NewTable(ReportSpec spec, IEnumerable<ReportColumn> columns) =>
            new()
            {
                Title = $"{ReportSpec.SubjectName(spec.Subject)} {ReportSpec.GroupingName(spec.GroupBy)}",
                PeriodText = ReportSpec.UsesPeriod(spec.Subject)
                    ? $"من {spec.From:yyyy/MM/dd} إلى {spec.To:yyyy/MM/dd}"
                    : "الحالة الحالية",
                LabelHeader = ReportSpec.GroupingLabel(spec.GroupBy),
                Columns = columns.ToList()
            };
    }
}
