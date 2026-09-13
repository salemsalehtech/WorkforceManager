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
        private readonly ScrapService _scrap;
        private readonly ProductionStageOutputService _productionOutput;

        public ReportBuilderService(
            IDailyProductionRepository production,
            IAttendanceRepository attendance,
            IPenaltyRepository penalties,
            IWageAdjustmentRepository adjustments,
            IHourlyWorkLogRepository hourly,
            IWorkerRepository workers,
            IProductRepository products,
            PayrollService payroll,
            ScrapService scrap,
            ProductionStageOutputService productionOutput)
        {
            _production = production;
            _attendance = attendance;
            _penalties = penalties;
            _adjustments = adjustments;
            _hourly = hourly;
            _workers = workers;
            _products = products;
            _payroll = payroll;
            _scrap = scrap;
            _productionOutput = productionOutput;
        }

        public async Task<ReportTable> BuildAsync(ReportSpec spec)
        {
            var table = await BuildTableAsync(spec);

            if (spec.CompareWithPrevious && ReportSpec.UsesPeriod(spec.Subject))
                await AddPreviousPeriodAsync(table, spec);

            // الترتيب قبل التخطيط عشان ينفع بعمود مخفي، والاتنين قبل
            // الإجماليات عشان الإجمالي يطلع على الأعمدة والصفوف
            // المعروضة فعلاً
            table = table
                .ApplySort(spec.SortKey, spec.SortDescending, spec.TopN)
                .ApplyLayout(spec.ColumnLayout);

            return spec.ShowTotals ? table.WithTotals() : table;
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
                // عمود "القطع" اسمه بيتغيّر حسب معناه — نفس فكرة CountsCompletedOutput:
                // بالمنتج/اليوم/الأسبوع رقم حقيقي (الإنتاج الفعلي)، وبالعامل/بالمرحلة
                // عدد ضربات العامل على المكنة مش قطع منتج فعلية. القرار هنا بيشوف
                // مستوى التجميع الأساسي بس (المستوى الثانوي لسه مش مختار وقت
                // عرض قايمة الأعمدة المتاحة) — الاسم النهائي في التقرير الفعلي
                // بيتحدد في ProductionAsync اللي شايف كل المستويات.
                ReportSubject.Production => new[]
                {
                    new ReportColumn
                    {
                        Header = grouping is ReportGrouping.Worker or ReportGrouping.Stage
                            ? "عدد الضربات" : "القطع",
                        Key = "pieces", Kind = ReportValueKind.Whole, Sums = true
                    },
                    new ReportColumn { Header = "اليوميات", Key = "workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "عدد العمال", Key = "workers", Kind = ReportValueKind.Whole },
                    new ReportColumn { Header = "أيام فيها شغل", Key = "workdays_with_work", Kind = ReportValueKind.Whole },
                    new ReportColumn { Header = "هالك", Key = "scrap", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "نسبة الهالك %", Key = "scrap_rate", Kind = ReportValueKind.Fraction }
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
                ReportSubject.Scrap => new[]
                {
                    new ReportColumn { Header = "قطع الهالك", Key = "scrap_pieces", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "عدد المرات", Key = "scrap_entries", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "أيام فيها هالك", Key = "scrap_days", Kind = ReportValueKind.Whole }
                },

                // حساب إداري: حاضر تلقائيًا كل يوم فمفيش عمود غياب —
                // السؤال الوحيد اللي يفرق هو اليوميات (فيها سهر ولا لأ) والأجر
                ReportSubject.DepartmentAccounts => new[]
                {
                    new ReportColumn { Header = "أيام الحضور", Key = "present_days", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "اليوميات", Key = "workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "الأجر", Key = "wage", Kind = ReportValueKind.Money, Sums = true }
                },

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
                ReportSubject.Scrap => await ScrapDetailAsync(spec),

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
                    // سجل خام = ضربات عامل واحد، مش إنتاج فعلي — الاسم دايمًا
                    // "الضربات" هنا، بصرف النظر عن أي تجميع
                    new ReportColumn { Header = "عدد الضربات", Key = "pieces", Kind = ReportValueKind.Whole, Sums = true },
                    new ReportColumn { Header = "اليوميات", Key = "workdays", Kind = ReportValueKind.Fraction, Sums = true },
                    new ReportColumn { Header = "اليومية وقت التسجيل", Key = "quota", Kind = ReportValueKind.Whole }
                }
            };

            foreach (var r in rows.OrderBy(r => r.Date).ThenBy(r => r.Worker?.SortOrder))
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

        /// <summary>
        /// تقرير الهالك: القطع اللي اتشالت من الخط، متجمّعة بالمنتج أو
        /// المرحلة أو **السبب** أو اليوم.
        ///
        /// التجميع بالسبب هو اللي بيجاوب على "الهالك راح فين؟" — وهو
        /// السؤال اللي التقرير ده موجود عشانه.
        /// </summary>
        private async Task<ReportTable> ScrapAsync(ReportSpec spec)
        {
            var records = FilterScrap(await _scrap.GetByRangeAsync(spec.From, spec.To), spec);

            var groups = records
                .GroupBy(s => ScrapKey(s, spec.GroupBy))
                .OrderByDescending(g => g.Sum(s => s.PieceCount))
                .ThenBy(g => g.Key.Label)
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));

            foreach (var g in groups)
                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    Values =
                    {
                        g.Sum(s => s.PieceCount),
                        g.Count(),
                        g.Select(s => s.Date.Date).Distinct().Count()
                    }
                });

            return table;
        }

        private static List<ScrapRecordDto> FilterScrap(
            IReadOnlyList<ScrapRecordDto> records, ReportSpec spec) =>
            records.Where(s =>
                    (spec.StageIds is not { Count: > 0 } || spec.StageIds.Contains(s.ProductionStageId)) &&
                    (spec.ProductIds is not { Count: > 0 } || spec.ProductIds.Contains(s.ProductId)))
                .ToList();

        private GroupKey ScrapKey(ScrapRecordDto record, ReportGrouping grouping) => grouping switch
        {
            ReportGrouping.Product => new GroupKey(record.ProductName, 0),
            ReportGrouping.Stage => new GroupKey($"{record.ProductName} — {record.StageName}", 0),
            ReportGrouping.Reason => new GroupKey(record.ReasonDisplay, 0),
            ReportGrouping.Week => WeekKey(record.Date),
            ReportGrouping.Month => MonthKey(record.Date),
            _ => DayKey(record.Date)
        };

        private async Task<ReportDetail> ScrapDetailAsync(ReportSpec spec)
        {
            var records = FilterScrap(await _scrap.GetByRangeAsync(spec.From, spec.To), spec);

            var detail = new ReportDetail
            {
                SheetName = "سجلات الهالك",
                Columns =
                {
                    Text("اليوم", "date"),
                    Text("المنتج", "product"),
                    Text("المرحلة", "stage"),
                    new ReportColumn { Header = "القطع", Key = "pieces", Kind = ReportValueKind.Whole, Sums = true },
                    Text("السبب", "reason"),
                    Text("ملاحظة", "note")
                }
            };

            foreach (var record in records.OrderBy(s => s.Date).ThenBy(s => s.ProductName))
                detail.Rows.Add(new ReportDetailRow
                {
                    GroupLabel = ScrapKey(record, spec.GroupBy).Label,
                    Cells =
                    {
                        ReportCell.Of(record.Date),
                        ReportCell.Of(record.ProductName),
                        ReportCell.Of(record.StageName),
                        ReportCell.Of(record.PieceCount),
                        ReportCell.Of(record.ReasonDisplay),
                        ReportCell.Of(record.Note)
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

            foreach (var w in workers.OrderBy(w => w.SortOrder))
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
            ReportSubject.Scrap => await ScrapAsync(spec),
            ReportSubject.DepartmentAccounts => await DepartmentAccountsAsync(spec),
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
        private static bool CountsCompletedOutput(IReadOnlyList<ReportGrouping> levels, ReportSpec spec)
        {
            if (levels.Any(l => l is ReportGrouping.Worker or ReportGrouping.Stage))
                return false;

            // فلتر بعامل معيّن معناه السؤال بقى "العامل ده عمل قد إيه"
            // مش "كام قطعة خلصت الخط" — نفس قاعدة التجميع بالعامل
            // بالظبط، حتى لو التجميع الظاهر نفسه بالمنتج/اليوم.
            // ProductionOutputRecordDto (مصدر "التام") أصلاً مالوش
            // WorkerId يتفلتر بيه — رقم جماعي بطبيعته، فمينفعش يتقصّ
            // على عامل واحد. لازم يرجع لمجموع سطور العامل ده بدل كده.
            if (spec.WorkerIds is { Count: > 0 })
                return false;

            return levels[0] is ReportGrouping.Product or ReportGrouping.Day or ReportGrouping.Week
                or ReportGrouping.Month;
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
            var lastStageIds = CountsCompletedOutput(levels, spec)
                ? ProductionLine
                    .LastStageIdByProduct(await _products.GetAllWithStagesAsync())
                    .Values.ToHashSet()
                : null;

            // مستويات التجميع الإضافية بتبقى أعمدة نصية قبل الأرقام،
            // فمحرّر الأعمدة يقدر يخفيها أو يرتّبها زي أي عمود
            var levelColumns = levels.Skip(1)
                .Select(level => Text(ReportSpec.GroupingLabel(level), LevelKey(level)))
                .ToList();

            // ColumnsFor بيقرر اسم "القطع"/"الضربات" من مستوى التجميع
            // الأساسي بس (هو كل اللي محرّر الأعمدة عارفه قبل التقرير).
            // هنا عارفين كل المستويات (زي بالمنتج وبعدين بالعامل)، فبنثبّت
            // الاسم على نفس القاعدة اللي CountsCompletedOutput بتحكم بيها
            // فعليًا — عشان الاسم يفضل مطابق للرقم في كل الحالات.
            var piecesHeader = lastStageIds is not null ? "القطع" : "عدد الضربات";
            var productionColumns = ColumnsFor(spec.Subject, spec.GroupBy)
                .Select(c => c.Key == "pieces" ? new ReportColumn
                {
                    Key = c.Key, Header = piecesHeader, Kind = c.Kind, Sums = c.Sums
                } : c);

            var table = NewTable(spec, levelColumns.Concat(productionColumns));

            // **الهالك مالوش عامل** (شوف ProductionScrap)، فالتقرير
            // المجمّع بالعامل عمود الهالك فيه بيفضل فاضي — شرطة معناها
            // "السؤال ده مالوش إجابة هنا"، مش صفر يوهم إن مفيش هالك.
            var scrapByGroup = levels.Contains(ReportGrouping.Worker)
                ? null
                : await ScrapByGroupAsync(spec, levels);

            // هالك آخر مرحلة لوحده — ده اللي بيتخصم من التام. الهالك في
            // نص الخط مبيأثرش عليه لأن القطع دي أصلاً ماوصلتش الآخر.
            var lastStageScrapByGroup = lastStageIds is null
                ? null
                : await ScrapByGroupAsync(spec, levels, lastStageIds);

            // "بلوك الإنتاج" (بالمنتج/اليوم/الأسبوع): القطع من الإنتاج
            // الفعلي (ProductionStageOutputService) مش من مجموع قطع
            // العمال — رقمين منفصلين تمامًا. "وحدة الشغل" (بالعامل/
            // بالمرحلة) تفضل من DailyProduction زي ما هي تحت في الحلقة.
            var productOutputByGroup = lastStageIds is null
                ? null
                : await ProductOutputByGroupAsync(spec, levels, lastStageIds);

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

                // لما العمود بيقيس **التام**، هالك آخر مرحلة بيتخصم منه:
                // القطعة اللي خلصت الخط والجودة رفضتها مش منتج تام.
                // ولما بيقيس الشغل المبذول، الهالك مالوش علاقة — العامل
                // اشتغل على القطعة فعلًا قبل ما تتشال.
                var pieces = lastStageIds is null
                    ? g.Sum(r => r.PieceCount)
                    : Math.Max(0,
                        (productOutputByGroup?.GetValueOrDefault(g.Key) ?? 0)
                        - (lastStageScrapByGroup?.GetValueOrDefault(g.Key) ?? 0));

                decimal? scrap = scrapByGroup is null
                    ? null
                    : scrapByGroup.TryGetValue(g.Key, out var s) ? s : 0;

                foreach (var value in new decimal?[]
                {
                    pieces,
                    g.Sum(r => r.WorkdaysCompleted),
                    g.Select(r => r.WorkerId).Distinct().Count(),
                    g.Select(r => r.Date.Date).Distinct().Count(),
                    scrap,
                    ScrapRate(pieces, scrap)
                })
                {
                    row.Values.Add(value);
                    row.Texts.Add(null);
                }

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// نسبة الهالك = الهالك ÷ (اللي طلع سليم + الهالك).
        ///
        /// المقام هو **كل اللي اتشتغل عليه وخرج**، فالنسبة بتجاوب على
        /// "من كل 100 قطعة اشتغلنا عليها، كام واحدة راحت؟". القسمة على
        /// السليم لوحده بتدّي رقم أكبر من الحقيقة.
        /// </summary>
        private static decimal? ScrapRate(decimal pieces, decimal? scrap)
        {
            if (scrap is null) return null;

            var handled = pieces + scrap.Value;
            return handled <= 0 ? null : Math.Round(scrap.Value / handled * 100m, 1);
        }

        /// <summary>
        /// الهالك موزّع على نفس مجموعات التقرير.
        ///
        /// سجل الهالك عنده مرحلة وتاريخ بس، فالمفتاح بيتبني من نفس
        /// دوال <see cref="Key"/> على الأبعاد اللي موجودة عنده — وده
        /// اللي بيخلي الرقم يقع في السطر الصح من غير حساب موازي.
        /// </summary>
        /// <param name="onlyStages">
        /// مراحل معيّنة بس (آخر مرحلة مثلاً) — null يعني كل المراحل.
        /// </param>
        private async Task<Dictionary<CompositeKey, int>> ScrapByGroupAsync(
            ReportSpec spec, IReadOnlyList<ReportGrouping> levels, HashSet<int>? onlyStages = null)
        {
            var records = await _scrap.GetByRangeAsync(spec.From, spec.To);

            var filtered = records.Where(s =>
                (onlyStages is null || onlyStages.Contains(s.ProductionStageId)) &&
                (spec.StageIds is not { Count: > 0 } || spec.StageIds.Contains(s.ProductionStageId)) &&
                (spec.ProductIds is not { Count: > 0 } || spec.ProductIds.Contains(s.ProductId)));

            var result = new Dictionary<CompositeKey, int>();

            foreach (var record in filtered)
            {
                var parts = levels.Select(level => level switch
                {
                    ReportGrouping.Product => record.ProductName,
                    ReportGrouping.Stage => $"{record.ProductName} — {record.StageName}",
                    ReportGrouping.Week => WeekKey(record.Date).Label,
                    ReportGrouping.Month => MonthKey(record.Date).Label,
                    _ => DayKey(record.Date).Label
                }).ToList();

                var key = new CompositeKey(parts);
                result[key] = result.TryGetValue(key, out var total) ? total + record.PieceCount : record.PieceCount;
            }

            return result;
        }

        /// <summary>
        /// الإنتاج الفعلي موزّع على نفس مجموعات التقرير — نفس بناء
        /// <see cref="ScrapByGroupAsync"/> بالحرف، مصدره
        /// <see cref="ProductionStageOutputService"/> مش <see cref="ScrapService"/>.
        /// شكل <see cref="ProductionOutputRecordDto"/> مطابق لـ
        /// <c>ScrapRecordDto</c> (ProductName/StageName/Date/ProductId/
        /// ProductionStageId/PieceCount) فمفتاح التجميع بيتبني بنفس الطريقة.
        /// </summary>
        private async Task<Dictionary<CompositeKey, int>> ProductOutputByGroupAsync(
            ReportSpec spec, IReadOnlyList<ReportGrouping> levels, HashSet<int>? onlyStages = null)
        {
            var records = await _productionOutput.GetByRangeAsync(spec.From, spec.To);

            var filtered = records.Where(r =>
                (onlyStages is null || onlyStages.Contains(r.ProductionStageId)) &&
                (spec.StageIds is not { Count: > 0 } || spec.StageIds.Contains(r.ProductionStageId)) &&
                (spec.ProductIds is not { Count: > 0 } || spec.ProductIds.Contains(r.ProductId)));

            var result = new Dictionary<CompositeKey, int>();

            foreach (var record in filtered)
            {
                var parts = levels.Select(level => level switch
                {
                    ReportGrouping.Product => record.ProductName,
                    ReportGrouping.Stage => $"{record.ProductName} — {record.StageName}",
                    ReportGrouping.Week => WeekKey(record.Date).Label,
                    ReportGrouping.Month => MonthKey(record.Date).Label,
                    _ => DayKey(record.Date).Label
                }).ToList();

                var key = new CompositeKey(parts);
                result[key] = result.TryGetValue(key, out var total) ? total + record.PieceCount : record.PieceCount;
            }

            return result;
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

                foreach (var w in workers.OrderByDescending(w => w.NetWageEgp).ThenBy(w => w.SortOrder))
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

        // ======================= الحسابات الإدارية =======================

        /// <summary>
        /// تقرير الحسابات الإدارية (مدير/رئيس قسم) — مصدر البيانات
        /// <see cref="IWorkerRepository.GetDepartmentAccountsAsync"/> بس،
        /// مفيش خلط مع تقارير العمال أبدًا. حاضرين تلقائيًا كل يوم
        /// (DepartmentAttendanceService)، فمفيش عمود غياب هنا — السؤال
        /// اللي التقرير ده بيجاوبه هو اليوميات (بتتغيّر لو سهروا) والأجر.
        /// </summary>
        private async Task<ReportTable> DepartmentAccountsAsync(ReportSpec spec)
        {
            var accounts = (await _workers.GetDepartmentAccountsAsync())
                .Where(a => spec.WorkerIds is not { Count: > 0 } || spec.WorkerIds.Contains(a.Id))
                .ToList();

            var table = NewTable(spec, ColumnsFor(spec.Subject, spec.GroupBy));
            if (accounts.Count == 0) return table;

            var accountIds = accounts.Select(a => a.Id).ToHashSet();
            var namesById = accounts.ToDictionary(a => a.Id, a => a.FullName);
            var wageById = accounts.ToDictionary(a => a.Id, a => a.DailyWageEgp);
            var roleById = accounts.ToDictionary(a => a.Id, a => a.HourlyRole!.Value);

            var attendance = (await _attendance.GetByRangeAsync(spec.From, spec.To))
                .Where(a => accountIds.Contains(a.WorkerId) && a.Status == AttendanceStatus.Present)
                .ToList();
            var hourly = (await _hourly.GetByRangeAsync(spec.From, spec.To))
                .Where(h => accountIds.Contains(h.WorkerId))
                .ToList();

            if (spec.GroupBy == ReportGrouping.Worker)
            {
                var presentByWorker = attendance.ToLookup(a => a.WorkerId);
                var hourlyByWorker = hourly.ToLookup(h => h.WorkerId);

                foreach (var accountId in accountIds.OrderBy(id => namesById[id]))
                {
                    var workdays = hourlyByWorker[accountId].Sum(h => h.WorkdaysCredited);

                    // مدير القسم مالوش راتب خالص — حقل الأجر بيتساب فاضي
                    // (null) مش صفر، عشان يبان "مالوش المفهوم ده" مش
                    // "أجره النهارده صفر". رئيس القسم بس هو اللي بياخد
                    // يومية عادية.
                    var role = roleById[accountId];
                    decimal? wage = role == HourlyRole.DepartmentManager ? null : workdays * wageById[accountId];

                    table.Rows.Add(new ReportRow
                    {
                        Label = $"{namesById[accountId]} ({role.ToArabicName()})",
                        Values = { presentByWorker[accountId].Count(), workdays, wage }
                    });
                }

                return table;
            }

            var groups = hourly
                .GroupBy(h => spec.GroupBy switch
                {
                    ReportGrouping.Week => WeekKey(h.Date),
                    ReportGrouping.Month => MonthKey(h.Date),
                    _ => DayKey(h.Date)
                })
                .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Label)
                .ToList();

            var presentByPeriod = attendance.ToLookup(a => spec.GroupBy switch
            {
                ReportGrouping.Week => WeekKey(a.Date),
                ReportGrouping.Month => MonthKey(a.Date),
                _ => DayKey(a.Date)
            });

            foreach (var g in groups)
            {
                var workdays = g.Sum(h => h.WorkdaysCredited);

                // مدير القسم مالوش راتب خالص — مبيدخلش في مجموع الأجر
                // حتى لو اتجمع بفترة بتضم أكتر من حساب
                var wage = g.Where(h => roleById[h.WorkerId] != HourlyRole.DepartmentManager)
                    .Sum(h => h.WorkdaysCredited * wageById.GetValueOrDefault(h.WorkerId, 0m));

                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    Values = { presentByPeriod[g.Key].Count(), workdays, wage }
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
                             .OrderByDescending(w => w.Skills.Count).ThenBy(w => w.SortOrder))
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

        /// <summary>
        /// ترتيب الصفوف في التقرير بيتم بمقارنة نص <see cref="GroupKey.Label"/>
        /// نفسه (شوف <c>CompositeKey</c> واستخدامه في ProductionAsync) — الـ
        /// Order هنا مش بيتقرا. لازم يبدأ بصيغة تترتّب أبجديًا زي تاريخها
        /// بالظبط (نفس "yyyy/MM/dd" بتاعة DayKey)، عشان كده "yyyy/MM" مش
        /// اسم الشهر العربي — ده كان بيرتّب "أغسطس" قبل "يوليو" لأنه أبجديًا كذا.
        /// </summary>
        private static GroupKey MonthKey(DateTime date) =>
            new(date.ToString("yyyy/MM"), date.Year * 12 + date.Month);

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
            ReportGrouping.Month => MonthKey(row.Date),
            _ => DayKey(row.Date)
        };

        private static List<DailyProduction> Filter(IReadOnlyList<DailyProduction> rows, ReportSpec spec) =>
            rows.Where(r =>
                    (spec.WorkerIds is not { Count: > 0 } || spec.WorkerIds.Contains(r.WorkerId)) &&
                    (spec.StageIds is not { Count: > 0 } || spec.StageIds.Contains(r.ProductionStageId)) &&
                    (spec.ProductIds is not { Count: > 0 } ||
                     spec.ProductIds.Contains(r.ProductionStage?.ProductId ?? 0)))
                .ToList();

        /// <summary>
        /// دايمًا مجموعة حقيقية (مش null) — الحسابات الإدارية (مدير/رئيس
        /// قسم) مستبعدة هنا بالتبعية لأن GetAllWithSkillsAsync ذات نفسه
        /// بيستبعدهم دايمًا، حتى لو المستخدم مفعّل مفيش فلتر عمال خالص.
        /// تقارير العمال (حضور/أجور/جزاءات/سلف وحوافز) لازم تفضل نظيفة
        /// منهم تمامًا — تقريرهم منفصل (ReportSubject.DepartmentAccounts).
        ///
        /// فلتر منتج/مرحلة هنا معناه "مين اشتغل عليه في المدة دي؟" —
        /// مش تقييد رقم الأجر/الحضور نفسه بمنتج واحد (الأجر عن شغل
        /// العامل كله في المدة، مهما كان عدد المنتجات). قبل السطرين
        /// الجايين كان الفلترين دول بيوصلوا هنا وميعملوا حاجة خالص —
        /// كشف الأجور كان بيرجع كل العمال بصرف النظر عن المنتج/المرحلة
        /// المُعلّمة، وده اللي كان بيبان "الفلتر مش شغال".
        /// </summary>
        private async Task<HashSet<int>?> AllowedWorkerIdsAsync(ReportSpec spec)
        {
            var workers = await _workers.GetAllWithSkillsAsync();
            var allowed = workers.Where(w => Matches(w, spec)).Select(w => w.Id).ToHashSet();

            if (spec.ProductIds is { Count: > 0 } || spec.StageIds is { Count: > 0 })
            {
                var worked = Filter(await _production.GetByRangeAsync(spec.From, spec.To), spec)
                    .Select(r => r.WorkerId)
                    .ToHashSet();
                allowed.IntersectWith(worked);
            }

            return allowed;
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
