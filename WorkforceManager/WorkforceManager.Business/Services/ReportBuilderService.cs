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
            var table = spec.Subject switch
            {
                ReportSubject.Production => await ProductionAsync(spec),
                ReportSubject.Attendance => await AttendanceAsync(spec),
                ReportSubject.Wages => await WagesAsync(spec),
                ReportSubject.Penalties => await PenaltiesAsync(spec),
                ReportSubject.WageAdjustments => await AdjustmentsAsync(spec),
                ReportSubject.Skills => await SkillsAsync(spec),
                _ => throw new InvalidOperationException("موضوع تقرير غير معروف")
            };

            return table.WithTotals();
        }

        // ======================= الإنتاج =======================

        private async Task<ReportTable> ProductionAsync(ReportSpec spec)
        {
            var rows = Filter(await _production.GetByRangeAsync(spec.From, spec.To), spec);

            // القطع بتتجمع على كل المراحل عن قصد هنا: التقرير ده بيقيس
            // **الشغل المبذول** مش إنتاج المنتج. القطعة اللي عدّت على
            // 11 مرحلة اشتغل فيها 11 عامل، وكل واحد ليه حقه.
            // "إنتاج المنتج التام" سؤال تاني بيتجاوب في التجميع بالمنتج.
            var groups = rows
                .GroupBy(r => Key(r, spec.GroupBy))
                .OrderBy(g => g.Key.Order).ThenBy(g => g.Key.Label)
                .ToList();

            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "القطع", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "اليوميات", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "عدد العمال", Kind = ReportValueKind.Whole },
                new ReportColumn { Header = "أيام فيها شغل", Kind = ReportValueKind.Whole }
            });

            foreach (var g in groups)
                table.Rows.Add(new ReportRow
                {
                    Label = g.Key.Label,
                    Values =
                    {
                        g.Sum(r => r.PieceCount),
                        g.Sum(r => r.WorkdaysCompleted),
                        g.Select(r => r.WorkerId).Distinct().Count(),
                        g.Select(r => r.Date.Date).Distinct().Count()
                    }
                });

            return table;
        }

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

            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "حضور", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "غياب بإذن", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "غياب بدون إذن", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "خصم الغياب (يومية)", Kind = ReportValueKind.Fraction, Sums = true }
            });

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
            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "يوميات منتجة", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "خصم غياب", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "خصم جزاءات", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "صافي اليوميات", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "حوافز", Kind = ReportValueKind.Money, Sums = true },
                new ReportColumn { Header = "سلف", Kind = ReportValueKind.Money, Sums = true },
                new ReportColumn { Header = "الأجر النهائي", Kind = ReportValueKind.Money, Sums = true }
            });

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

            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "عدد الجزاءات", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "إجمالي الخصم (يومية)", Kind = ReportValueKind.Fraction, Sums = true },
                new ReportColumn { Header = "منها تلقائي (غياب)", Kind = ReportValueKind.Whole, Sums = true }
            });

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

            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "سلف", Kind = ReportValueKind.Money, Sums = true },
                new ReportColumn { Header = "حوافز", Kind = ReportValueKind.Money, Sums = true },
                new ReportColumn { Header = "الصافي", Kind = ReportValueKind.Money, Sums = true }
            });

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

            var table = NewTable(spec, new[]
            {
                new ReportColumn { Header = "عدد المهارات", Kind = ReportValueKind.Whole, Sums = true },
                new ReportColumn { Header = "متوسط النجوم", Kind = ReportValueKind.Fraction },
                new ReportColumn { Header = "منتجات بيعرفها", Kind = ReportValueKind.Whole }
            });

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

            table.Columns[0] = new ReportColumn
            { Header = "عمال مؤهلين", Kind = ReportValueKind.Whole, Sums = true };
            table.Columns[2] = new ReportColumn
            { Header = "عدد المراحل", Kind = ReportValueKind.Whole, Sums = true };

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
                LabelHeader = spec.GroupBy switch
                {
                    ReportGrouping.Worker => "العامل",
                    ReportGrouping.Product => "المنتج",
                    ReportGrouping.Stage => "المرحلة",
                    ReportGrouping.Week => "الأسبوع",
                    _ => "اليوم"
                },
                Columns = columns.ToList()
            };
    }
}
