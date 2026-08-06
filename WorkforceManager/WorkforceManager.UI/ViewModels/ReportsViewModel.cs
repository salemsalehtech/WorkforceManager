using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة التقارير والتقييم، وفيها تبويبين:
    /// 1) تقييم اليوم: كل عامل مقارن بمتوسط زمايله اللي أنتجوا في نفس
    ///    اليوم، بتصنيف ملوّن (الأفضل / فوق المتوسط / متوسط / تحت
    ///    المتوسط / غياب بدون إذن)، مع تفاصيل إنتاجه وجزاءاته.
    /// 2) كشف الأسبوع: الترتيب النهائي بصافي اليوميات (بعد كل الخصومات)
    ///    مع تنقّل بين الأسابيع وتصدير الكشف لملف Excel منسّق.
    /// </summary>
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ReportsViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>أول تحميل للشاشة: تقرير النهارده + كشف الأسبوع الحالي + رسم المنتجات + كشف أجور الشهر + التقرير العام + قائمة العمال</summary>
        public async Task InitializeAsync()
        {
            await LoadDailyAsync();
            await LoadOutputAsync();
            await LoadWeeklyAsync();
            await LoadChartAsync();
            await LoadPayrollAsync();
            await LoadWorkersListAsync();
            await LoadGeneralReportAsync();
        }

        /// <summary>حساب مدى التاريخ للأزرار السريعة (اليوم/الأسبوع/الشهر)</summary>
        private static (DateTime from, DateTime to) ResolveQuickPeriod(string period)
        {
            var today = DateTime.Today;
            return period switch
            {
                "week" => WeeklySummaryService.GetWorkWeekRange(today), // أسبوع العمل: خميس → أربع
                "month" => (new DateTime(today.Year, today.Month, 1), today), // من أول الشهر لليوم
                _ => (today, today) // اليوم
            };
        }

        // ======================= تبويب تقييم اليوم =======================

        [ObservableProperty]
        private DateTime _dailyDate = DateTime.Today;

        partial void OnDailyDateChanged(DateTime value)
        {
            // تغيير اليوم بيعيد تحميل التقرير (وأي خطأ بيظهر مش بيضيع بصمت)
            SafeAsync.Run(LoadDailyAsync);
        }

        /// <summary>سطر ملخص فوق الجدول: متوسط الفريق وعدد المنتجين</summary>
        [ObservableProperty]
        private string _dailySummaryText = string.Empty;

        public ObservableCollection<DailyReportRow> DailyRows { get; } = new();

        // ------- لوحة "اليوم في سطر" -------
        // خمس حقايق المدير بيسأل عنها أول ما يفتح الشاشة: اشتغلنا على
        // إيه، أنتجنا كام، أكتر منتج، وأحسن وأقل عامل. كلها محسوبة من
        // سجلات اليوم — مفيش رقم مكتوب بالإيد.

        /// <summary>المنتجات اللي اتشغل عليها النهارده، مرتبة بالأكتر إنتاجًا</summary>
        public ObservableCollection<DailyProductRow> DailyProducts { get; } = new();

        public bool HasDailyProducts => DailyProducts.Count > 0;

        [ObservableProperty]
        private string _dailyTotalPiecesText = "0";

        [ObservableProperty]
        private string _dailyTopProductText = "—";

        [ObservableProperty]
        private string _dailyTopProductPiecesText = "";

        [ObservableProperty]
        private string _dailyBestWorkerText = "—";

        [ObservableProperty]
        private string _dailyBestWorkerDetail = "";

        [ObservableProperty]
        private string _dailyWorstWorkerText = "—";

        [ObservableProperty]
        private string _dailyWorstWorkerDetail = "";

        /// <summary>
        /// عامل واحد بس أنتج — "الأحسن" و"الأقل" هيبقوا هو، وعرضهم
        /// مرتين بيوحي بمقارنة محصلتش
        /// </summary>
        [ObservableProperty]
        private bool _dailyHasComparison;

        private async Task LoadDailyAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var evaluationService = scope.ServiceProvider.GetRequiredService<PerformanceEvaluationService>();
            var penaltyService = scope.ServiceProvider.GetRequiredService<PenaltyService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ProductActivityService>();

            var evaluations = await evaluationService.EvaluateDayAsync(DailyDate);

            // منتجات اليوم من نفس خدمة نشاط المنتجات اللي شاشة المنتجات
            // شغالة بيها — مفيش حساب تاني موازي
            var activity = (await activityService.GetAsync(DailyDate, DailyDate))
                .Where(p => p.WorkedInPeriod)
                .ToList();

            DailyProducts.Clear();
            foreach (var product in activity)
                DailyProducts.Add(new DailyProductRow
                {
                    ProductName = product.ProductName,
                    Pieces = product.PiecesProduced,
                    WorkerCount = product.WorkerIds.Count
                });

            OnPropertyChanged(nameof(HasDailyProducts));

            DailyTotalPiecesText = activity.Sum(p => p.PiecesProduced).ToString("N0");

            var top = activity.FirstOrDefault(); // الخدمة بترجّعهم مرتبين بالأكتر
            DailyTopProductText = top?.ProductName ?? "—";
            DailyTopProductPiecesText = top is null ? "" : $"{top.PiecesProduced:N0} قطعة";

            RefreshDailyExtremes(evaluations);
            // جزاءات اليوم بتتضم للعرض (مش جزء من تقييم الأداء نفسه)
            var penaltiesByWorker = (await penaltyService.GetPenaltiesByDateAsync(DailyDate))
                .GroupBy(p => p.WorkerId)
                .ToDictionary(g => g.Key,
                    g => string.Join("، ", g.Select(p => $"{p.Reason} ({p.Deduction.ToArabicName()})")));

            DailyRows.Clear();
            foreach (var e in evaluations)
            {
                penaltiesByWorker.TryGetValue(e.WorkerId, out var penaltiesText);
                DailyRows.Add(DailyReportRow.From(e, penaltiesText ?? ""));
            }

            var producers = evaluations.Where(e => e.TotalPieces > 0).ToList();
            DailySummaryText = producers.Count == 0
                ? "لا يوجد إنتاج مسجّل في هذا اليوم"
                : $"عدد المنتجين: {producers.Count} عامل   |   متوسط الفريق: {producers[0].TeamAverageWorkdays:0.##} يومية";
        }

        /// <summary>
        /// أحسن وأقل عامل في اليوم — من العمال اللي **أنتجوا فعلاً** بس.
        ///
        /// الغايبين واللي محصلش لهم تسجيل مستبعدين عن قصد: "أقل عامل"
        /// اللي إنتاجه صفر لأنه أجازة مش معلومة، هي تشويش. المقارنة بين
        /// اللي اشتغلوا هي اللي بتقول حاجة.
        /// </summary>
        private void RefreshDailyExtremes(IReadOnlyList<WorkerDailySummaryDto> evaluations)
        {
            var producers = evaluations
                .Where(e => e.TotalPieces > 0)
                .OrderByDescending(e => e.TotalWorkdays)
                .ThenBy(e => e.WorkerName)
                .ToList();

            DailyHasComparison = producers.Count > 1;

            if (producers.Count == 0)
            {
                DailyBestWorkerText = DailyWorstWorkerText = "—";
                DailyBestWorkerDetail = DailyWorstWorkerDetail = "مفيش إنتاج مسجّل";
                return;
            }

            var best = producers[0];
            DailyBestWorkerText = best.WorkerName;
            DailyBestWorkerDetail = $"{best.TotalWorkdays:0.##} يومية — {best.TotalPieces:N0} قطعة";

            var worst = producers[^1];
            DailyWorstWorkerText = worst.WorkerName;
            DailyWorstWorkerDetail = $"{worst.TotalWorkdays:0.##} يومية — {worst.TotalPieces:N0} قطعة";
        }

        // ======================= تبويب إنتاج اليوم (الدفعات) =======================

        [ObservableProperty]
        private DateTime _outputDate = DateTime.Today;

        partial void OnOutputDateChanged(DateTime value) => SafeAsync.Run(LoadOutputAsync);

        /// <summary>منتجات فيها حركة في اليوم ده (خلص منها أو دخل الخط)</summary>
        public ObservableCollection<DailyProductReportDto> OutputProducts { get; } = new();

        [ObservableProperty]
        private string _outputCompletedText = "0";

        [ObservableProperty]
        private string _outputStartedText = "0";

        [ObservableProperty]
        private bool _outputIsClosed;

        [ObservableProperty]
        private bool _outputIsEmpty = true;

        /// <summary>
        /// تقرير إنتاج اليوم: كام قطعة خلصت آخر مرحلة (= منتج تام) وكام قطعة
        /// دخلت أول مرحلة. الرقمين محسوبين من سجلات الإنتاج نفسها.
        /// </summary>
        private async Task LoadOutputAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DailyProductionReportService>();

            var report = await service.GetAsync(OutputDate);

            OutputProducts.Clear();
            foreach (var product in report.Products) OutputProducts.Add(product);

            OutputCompletedText = report.TotalCompletedPieces.ToString("N0");
            OutputStartedText = report.TotalStartedPieces.ToString("N0");
            OutputIsClosed = report.IsClosed;
            OutputIsEmpty = report.Products.Count == 0;
        }

        // ======================= تبويب كشف الأسبوع =======================

        /// <summary>أي تاريخ داخل الأسبوع المعروض — التنقل بيتحرك بيه 7 أيام</summary>
        private DateTime _weekAnchor = DateTime.Today;

        [ObservableProperty]
        private string _weekTitle = string.Empty;

        /// <summary>هل الأسبوع المعروض هو الأسبوع الحالي؟ (بيظهر بجانب العنوان)</summary>
        [ObservableProperty]
        private string _weekBadge = string.Empty;

        public ObservableCollection<WeeklyReportRow> WeeklyRows { get; } = new();

        [ObservableProperty]
        private WeeklyReportRow? _selectedWeeklyRow;

        private async Task LoadWeeklyAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();

            var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(_weekAnchor);
            WeekTitle = $"من الخميس {weekStart:yyyy/MM/dd} إلى الأربعاء {weekEnd:yyyy/MM/dd}";
            var (currentStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today);
            WeekBadge = weekStart == currentStart ? "(الأسبوع الحالي)" : "";

            var summaries = await weeklyService.GetTeamWeeklySummaryAsync(_weekAnchor);

            WeeklyRows.Clear();
            for (var i = 0; i < summaries.Count; i++)
                WeeklyRows.Add(WeeklyReportRow.From(summaries[i], rank: i + 1));

            SelectedWeeklyRow = WeeklyRows.FirstOrDefault();
        }

        [RelayCommand]
        private Task PreviousWeekAsync()
        {
            _weekAnchor = _weekAnchor.AddDays(-7);
            return LoadWeeklyAsync();
        }

        [RelayCommand]
        private Task NextWeekAsync()
        {
            _weekAnchor = _weekAnchor.AddDays(7);
            return LoadWeeklyAsync();
        }

        [RelayCommand]
        private Task CurrentWeekAsync()
        {
            _weekAnchor = DateTime.Today;
            return LoadWeeklyAsync();
        }

        // ======================= تبويب رسم إنتاج المنتجات =======================

        /// <summary>خيارات مدة الرسم (بالأسابيع، منتهية بالأسبوع الحالي)</summary>
        public List<int> ChartWeeksOptions { get; } = new() { 4, 8, 12, 24 };

        [ObservableProperty]
        private int _selectedChartWeeks = 8;

        partial void OnSelectedChartWeeksChanged(int value)
        {
            SafeAsync.Run(LoadChartAsync);
        }

        /// <summary>أعمدة الرسم مجمعة بالأسبوع (بالترتيب الزمني)</summary>
        public ObservableCollection<ChartWeekGroup> ChartWeekGroups { get; } = new();

        /// <summary>مفتاح الألوان: منتج → لون + إجمالي الفترة</summary>
        public ObservableCollection<ChartLegendItem> ChartLegend { get; } = new();

        [ObservableProperty]
        private bool _chartHasData;

        /// <summary>لوحة ألوان السلاسل — كل منتج بياخد لون ثابت طول الرسمة</summary>
        private static readonly string[] ChartPalette =
        {
            "#1F3864", "#0B6E4F", "#B7791F", "#7A3B8F",
            "#B00020", "#0F7B8A", "#C2563B", "#5B6B7C"
        };

        /// <summary>أقصى ارتفاع للعمود بالبكسل — الباقي بيتحسب نسبيًا عليه</summary>
        private const double MaxBarHeight = 190;

        private async Task LoadChartAsync()
        {
            List<ProductWeeklyPointDto> points;
            using (var scope = _scopeFactory.CreateScope())
            {
                var chartService = scope.ServiceProvider.GetRequiredService<ProductionChartService>();
                var to = DateTime.Today;
                var from = to.AddDays(-7 * (SelectedChartWeeks - 1));
                points = await chartService.GetProductWeeklyCompletedAsync(from, to);
            }

            // المنتجات اللي ليها إنتاج مكتمل في الفترة — الأكتر إنتاجًا الأول، ولون ثابت لكل منتج
            var productTotals = points
                .GroupBy(p => (p.ProductId, p.ProductName))
                .Select(g => (g.Key.ProductId, g.Key.ProductName, Total: g.Sum(x => x.CompletedPieces)))
                .OrderByDescending(x => x.Total)
                .ToList();

            var colorByProduct = productTotals
                .Select((p, i) => (p.ProductId, Color: ChartPalette[i % ChartPalette.Length]))
                .ToDictionary(x => x.ProductId, x => x.Color);

            ChartLegend.Clear();
            foreach (var p in productTotals)
            {
                ChartLegend.Add(new ChartLegendItem
                {
                    Color = colorByProduct[p.ProductId],
                    ProductName = p.ProductName,
                    TotalText = $"{p.Total:N0} قطعة"
                });
            }

            // كل أسابيع الفترة بالترتيب الزمني (حتى الفاضية — محور الزمن لازم يكون متصل)
            var (firstWeekStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today.AddDays(-7 * (SelectedChartWeeks - 1)));
            var pointsByWeek = points.ToLookup(p => p.WeekStart);
            var maxPieces = points.Count == 0 ? 1 : points.Max(p => p.CompletedPieces);

            ChartWeekGroups.Clear();
            for (var week = firstWeekStart; week <= DateTime.Today; week = week.AddDays(7))
            {
                var weekPoints = pointsByWeek[week]
                    .OrderBy(p => productTotals.FindIndex(t => t.ProductId == p.ProductId))
                    .ToList();

                ChartWeekGroups.Add(new ChartWeekGroup
                {
                    WeekLabel = $"{week:dd/MM}",
                    TotalText = weekPoints.Count == 0 ? "—" : $"{weekPoints.Sum(p => p.CompletedPieces):N0}",
                    Bars = weekPoints.Select(p => new ChartBar
                    {
                        Color = colorByProduct[p.ProductId],
                        // ارتفاع نسبي على أعلى قيمة في الفترة (بحد أدنى مرئي للقيم الصغيرة)
                        Height = Math.Max(4, (double)p.CompletedPieces / maxPieces * MaxBarHeight),
                        Tooltip = $"{p.ProductName}\nأسبوع {p.WeekStart:dd/MM} - {p.WeekEnd:dd/MM}\n{p.CompletedPieces:N0} قطعة مكتملة"
                    }).ToList()
                });
            }

            ChartHasData = points.Count > 0;
        }

        // ======================= تبويب كشف أجور الفترة (شهري) =======================

        /// <summary>بداية الفترة (افتراضيًا أول الشهر الحالي)</summary>
        [ObservableProperty]
        private DateTime _payrollFrom = new(DateTime.Today.Year, DateTime.Today.Month, 1);

        /// <summary>نهاية الفترة (افتراضيًا النهاردة)</summary>
        [ObservableProperty]
        private DateTime _payrollTo = DateTime.Today;

        [ObservableProperty]
        private string _payrollTotalText = "";

        public ObservableCollection<PayrollRow> PayrollRows { get; } = new();

        [RelayCommand]
        private Task RefreshPayrollAsync() => LoadPayrollAsync();

        private async Task LoadPayrollAsync()
        {
            PeriodPayrollDto period;
            using (var scope = _scopeFactory.CreateScope())
            {
                var payrollService = scope.ServiceProvider.GetRequiredService<PayrollService>();
                period = await payrollService.GetPeriodPayrollAsync(PayrollFrom, PayrollTo);
            }

            PayrollRows.Clear();
            var rank = 1;
            foreach (var w in period.Workers)
                PayrollRows.Add(PayrollRow.From(w, rank++));

            var days = (PayrollTo.Date - PayrollFrom.Date).Days + 1;
            PayrollTotalText = $"من {PayrollFrom:yyyy/MM/dd} إلى {PayrollTo:yyyy/MM/dd} ({days} يوم)   |   " +
                $"إجمالي الأجور: {period.TotalWageEgp:N0} جنيه   |   إجمالي اليوميات: {period.TotalNetWorkdays:0.##}";
        }

        [RelayCommand]
        private async Task ExportPayrollAsync()
        {
            if (PayrollRows.Count == 0)
            {
                MessageBox.Show("لا توجد بيانات في الفترة دي للتصدير", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "حفظ كشف أجور الفترة",
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"كشف أجور {PayrollFrom:yyyy-MM-dd} إلى {PayrollTo:yyyy-MM-dd}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                PeriodPayrollDto period;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var payrollService = scope.ServiceProvider.GetRequiredService<PayrollService>();
                    var excelService = scope.ServiceProvider.GetRequiredService<WeeklyReportExcelService>();
                    period = await payrollService.GetPeriodPayrollAsync(PayrollFrom, PayrollTo);
                    excelService.ExportPeriodPayroll(period, dialog.FileName);
                }

                var open = MessageBox.Show(
                    $"تم حفظ كشف الأجور:\n{dialog.FileName}\n\nفتح الملف الآن؟",
                    "تم التصدير", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ الملف:\n{ex.Message}", "خطأ في التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ======================= تبويب التقرير العام للإنتاج =======================

        /// <summary>بداية فترة التقرير العام (افتراضيًا أول الشهر)</summary>
        [ObservableProperty]
        private DateTime _generalFrom = new(DateTime.Today.Year, DateTime.Today.Month, 1);

        /// <summary>نهاية فترة التقرير العام (افتراضيًا النهاردة)</summary>
        [ObservableProperty]
        private DateTime _generalTo = DateTime.Today;

        /// <summary>سطر الملخص الإجمالي للقسم فوق الجداول</summary>
        [ObservableProperty]
        private string _generalSummaryText = "";

        /// <summary>تفصيل الإنتاج بالمنتج/المرحلة</summary>
        public ObservableCollection<GeneralStageRow> GeneralByProductStage { get; } = new();

        /// <summary>تفصيل الإنتاج بالعامل (مرتّب باليوميات)</summary>
        public ObservableCollection<GeneralWorkerRow> GeneralByWorker { get; } = new();

        [RelayCommand]
        private Task RefreshGeneralAsync() => LoadGeneralReportAsync();

        /// <summary>زر سريع (اليوم/الأسبوع/الشهر) يضبط المدى ويعيد التحميل</summary>
        [RelayCommand]
        private Task GeneralPeriodAsync(string period)
        {
            (GeneralFrom, GeneralTo) = ResolveQuickPeriod(period);
            return LoadGeneralReportAsync();
        }

        private async Task LoadGeneralReportAsync()
        {
            GeneralProductionReportDto report;
            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ProductionReportService>();
                report = await service.GetGeneralReportAsync(GeneralFrom, GeneralTo);
            }

            GeneralByProductStage.Clear();
            foreach (var s in report.ByProductStage)
                GeneralByProductStage.Add(GeneralStageRow.From(s));

            GeneralByWorker.Clear();
            var rank = 1;
            foreach (var w in report.ByWorker)
                GeneralByWorker.Add(GeneralWorkerRow.From(w, rank++));

            var days = (GeneralTo.Date - GeneralFrom.Date).Days + 1;
            GeneralSummaryText =
                $"من {report.From:yyyy/MM/dd} إلى {report.To:yyyy/MM/dd} ({days} يوم)   |   " +
                $"قطع مكتملة: {report.TotalCompletedPieces:N0}   |   إجمالي اليوميات: {report.TotalWorkdays:0.##}   |   " +
                $"عدد العمال: {report.WorkersCount}   |   أيام الإنتاج: {report.ProductionDays}";
        }

        [RelayCommand]
        private async Task ExportGeneralAsync()
        {
            if (GeneralByProductStage.Count == 0 && GeneralByWorker.Count == 0)
            {
                MessageBox.Show("لا يوجد إنتاج في الفترة دي للتصدير", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "حفظ التقرير العام للإنتاج",
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"تقرير إنتاج {GeneralFrom:yyyy-MM-dd} إلى {GeneralTo:yyyy-MM-dd}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProductionReportService>();
                var excelService = scope.ServiceProvider.GetRequiredService<WeeklyReportExcelService>();
                var report = await service.GetGeneralReportAsync(GeneralFrom, GeneralTo);
                excelService.ExportGeneralReport(report, dialog.FileName);

                var open = MessageBox.Show(
                    $"تم حفظ التقرير:\n{dialog.FileName}\n\nفتح الملف الآن؟",
                    "تم التصدير", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ الملف:\n{ex.Message}", "خطأ في التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ======================= تبويب تقرير عامل معيّن =======================

        /// <summary>قائمة العمال للاختيار منها</summary>
        public ObservableCollection<WorkerPickItem> Workers { get; } = new();

        [ObservableProperty]
        private WorkerPickItem? _selectedWorker;

        partial void OnSelectedWorkerChanged(WorkerPickItem? value)
        {
            // اختيار عامل جديد بيحمّل تقريره فورًا
            if (value is not null) SafeAsync.Run(LoadWorkerReportAsync);
        }

        /// <summary>بداية فترة تقرير العامل (افتراضيًا أول الشهر)</summary>
        [ObservableProperty]
        private DateTime _workerFrom = new(DateTime.Today.Year, DateTime.Today.Month, 1);

        /// <summary>نهاية فترة تقرير العامل (افتراضيًا النهاردة)</summary>
        [ObservableProperty]
        private DateTime _workerTo = DateTime.Today;

        /// <summary>ملخص التقرير (اسم العامل + النوع + الفترة)</summary>
        [ObservableProperty]
        private string _workerReportHeader = "اختر عامل لعرض تقريره";

        /// <summary>سطر أرقام الإنتاج والحضور</summary>
        [ObservableProperty]
        private string _workerProductionText = "";

        /// <summary>سطر الأجر والخصومات (بارز)</summary>
        [ObservableProperty]
        private string _workerWageText = "";

        /// <summary>هل فيه تقرير معروض؟ (يتحكم في ظهور الأرقام)</summary>
        [ObservableProperty]
        private bool _hasWorkerReport;

        public ObservableCollection<GeneralStageRow> WorkerByProductStage { get; } = new();
        public ObservableCollection<WorkerDayRow> WorkerByDay { get; } = new();
        public ObservableCollection<WorkerPenaltyRow> WorkerPenalties { get; } = new();

        [RelayCommand]
        private Task RefreshWorkerAsync() => LoadWorkerReportAsync();

        [RelayCommand]
        private Task WorkerPeriodAsync(string period)
        {
            (WorkerFrom, WorkerTo) = ResolveQuickPeriod(period);
            return LoadWorkerReportAsync();
        }

        private async Task LoadWorkersListAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var workerRepo = scope.ServiceProvider.GetRequiredService<IWorkerRepository>();
            var workers = await workerRepo.GetActiveWithSkillsAsync();

            Workers.Clear();
            foreach (var w in workers.OrderBy(w => w.FullName))
                Workers.Add(new WorkerPickItem { Id = w.Id, Display = w.FullName });
        }

        private async Task LoadWorkerReportAsync()
        {
            if (SelectedWorker is null) return;

            WorkerProductionReportDto report;
            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ProductionReportService>();
                report = await service.GetWorkerReportAsync(SelectedWorker.Id, WorkerFrom, WorkerTo);
            }

            WorkerByProductStage.Clear();
            foreach (var s in report.ByProductStage)
                WorkerByProductStage.Add(GeneralStageRow.From(s));

            WorkerByDay.Clear();
            foreach (var d in report.ByDay)
                WorkerByDay.Add(WorkerDayRow.From(d));

            WorkerPenalties.Clear();
            foreach (var p in report.Penalties)
                WorkerPenalties.Add(WorkerPenaltyRow.From(p));

            var days = (WorkerTo.Date - WorkerFrom.Date).Days + 1;
            WorkerReportHeader =
                $"{report.WorkerName} — {report.TypeText}   |   " +
                $"من {report.From:yyyy/MM/dd} إلى {report.To:yyyy/MM/dd} ({days} يوم)";
            WorkerProductionText =
                $"إجمالي القطع: {report.TotalPieces:N0}   |   يوميات منتجة: {report.ProducedWorkdays:0.##}   |   " +
                $"حضور: {report.PresentDays}   |   غياب بإذن: {report.AbsentWithPermissionDays}   |   " +
                $"غياب بدون إذن: {report.AbsentWithoutPermissionDays} (خصم {report.AbsenceDeduction})";
            // سطر الأجر: أجر اليوميات + الحوافز − السلف = الأجر النهائي
            var adjParts = "";
            if (report.BonusEgp > 0) adjParts += $"   +   حوافز {report.BonusEgp:N0} ج";
            if (report.AdvanceEgp > 0) adjParts += $"   −   سلف {report.AdvanceEgp:N0} ج";
            WorkerWageText = report.DailyWageEgp > 0
                ? $"أجر اليوميات: {report.NetWorkdays:0.##} × {report.DailyWageEgp:N0} = {report.WorkdaysWageEgp:N0} ج{adjParts}   =   " +
                  $"الأجر النهائي {report.NetWageEgp:N0} ج   (خصم جزاءات: {report.PenaltyDeduction})"
                : $"صافي اليوميات: {report.NetWorkdays:0.##}   (خصم جزاءات: {report.PenaltyDeduction}){adjParts}   |   سعر اليومية غير محدد";
            HasWorkerReport = true;
        }

        [RelayCommand]
        private async Task PrintPayslipAsync()
        {
            if (SelectedWorker is null || !HasWorkerReport)
            {
                MessageBox.Show("اختر عامل الأول عشان تطبع قسيمته", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            WorkerProductionReportDto report;
            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ProductionReportService>();
                report = await service.GetWorkerReportAsync(SelectedWorker.Id, WorkerFrom, WorkerTo);
            }

            // معاينة القسيمة في نافذة، والطباعة من جواها لأي طابعة/PDF
            var window = new Views.PayslipWindow(PayslipData.From(report))
            {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        [RelayCommand]
        private async Task ExportWorkerAsync()
        {
            if (SelectedWorker is null || !HasWorkerReport)
            {
                MessageBox.Show("اختر عامل الأول عشان تصدّر تقريره", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "حفظ تقرير العامل",
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"تقرير {SelectedWorker.Display} {WorkerFrom:yyyy-MM-dd} إلى {WorkerTo:yyyy-MM-dd}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProductionReportService>();
                var excelService = scope.ServiceProvider.GetRequiredService<WeeklyReportExcelService>();
                var report = await service.GetWorkerReportAsync(SelectedWorker.Id, WorkerFrom, WorkerTo);
                excelService.ExportWorkerReport(report, dialog.FileName);

                var open = MessageBox.Show(
                    $"تم حفظ التقرير:\n{dialog.FileName}\n\nفتح الملف الآن؟",
                    "تم التصدير", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ الملف:\n{ex.Message}", "خطأ في التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ======================= تصدير Excel =======================

        [RelayCommand]
        private async Task ExportWeekAsync()
        {
            if (WeeklyRows.Count == 0)
            {
                MessageBox.Show("لا توجد بيانات في هذا الأسبوع للتصدير", "تنبيه",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (weekStart, _) = WeeklySummaryService.GetWorkWeekRange(_weekAnchor);
            var dialog = new SaveFileDialog
            {
                Title = "حفظ كشف الأسبوع",
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"كشف أسبوع {weekStart:yyyy-MM-dd}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var weeklyService = scope.ServiceProvider.GetRequiredService<WeeklySummaryService>();
                var excelService = scope.ServiceProvider.GetRequiredService<WeeklyReportExcelService>();

                // بنجيب البيانات طازة وقت التصدير (مش من صفوف العرض) — مصدر حقيقة واحد
                var summaries = await weeklyService.GetTeamWeeklySummaryAsync(_weekAnchor);
                excelService.ExportWeeklySummary(summaries, dialog.FileName);

                // عرض النتيجة مع خيار فتح الملف فورًا
                var open = MessageBox.Show(
                    $"تم حفظ الكشف بنجاح:\n{dialog.FileName}\n\nفتح الملف الآن؟",
                    "تم التصدير", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ الملف:\n{ex.Message}", "خطأ في التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ======================= نماذج العرض =======================

    /// <summary>منتج اتشغل عليه في اليوم — سطر في لوحة "اليوم في سطر"</summary>
    public class DailyProductRow
    {
        public string ProductName { get; init; } = "";
        public int Pieces { get; init; }
        public int WorkerCount { get; init; }

        public string PiecesText => $"{Pieces:N0} قطعة";
        public string WorkersText => $"{WorkerCount} عامل";
    }

    /// <summary>سطر واحد في جدول تقييم اليوم، بتصنيف ملوّن جاهز للعرض</summary>
    public class DailyReportRow
    {
        public string WorkerName { get; private init; } = "";
        public int TotalPieces { get; private init; }
        public decimal TotalWorkdays { get; private init; }
        public string PercentText { get; private init; } = "";
        public string RatingText { get; private init; } = "";
        public string RatingColor { get; private init; } = "#666666";
        public string AttendanceText { get; private init; } = "";
        public string BreakdownText { get; private init; } = "";
        public string PenaltiesText { get; private init; } = "";

        /// <summary>تحويل نتيجة التقييم من الخدمة لشكل العرض (نص + لون لكل تصنيف)</summary>
        public static DailyReportRow From(WorkerDailySummaryDto dto, string penaltiesText)
        {
            var (ratingText, ratingColor) = dto.Rating switch
            {
                PerformanceRating.TopPerformer => ("⭐ الأفضل النهارده", "#B7791F"),
                PerformanceRating.AboveAverage => ("فوق المتوسط", "#0B6E4F"),
                PerformanceRating.Average => ("متوسط", "#666666"),
                PerformanceRating.BelowAverage => ("تحت المتوسط", "#C62828"),
                PerformanceRating.UnexcusedAbsence => ("غياب بدون إذن", "#B00020"),
                _ => ("غير محدد", "#666666")
            };

            return new DailyReportRow
            {
                WorkerName = dto.WorkerName,
                TotalPieces = dto.TotalPieces,
                TotalWorkdays = dto.TotalWorkdays,
                // النسبة مالهاش معنى لو مفيش إنتاج أصلاً
                PercentText = dto.TotalPieces == 0 ? "—" : $"{dto.PercentVsAverage:+0.#;-0.#;0}%",
                RatingText = ratingText,
                RatingColor = ratingColor,
                AttendanceText = dto.AttendanceStatus switch
                {
                    Core.Enums.AttendanceStatus.Present => "حاضر",
                    Core.Enums.AttendanceStatus.AbsentWithPermission => "غياب بإذن",
                    Core.Enums.AttendanceStatus.AbsentWithoutPermission => "غياب بدون إذن",
                    _ => "—"
                },
                BreakdownText = string.Join("، ",
                    dto.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount}")),
                PenaltiesText = penaltiesText
            };
        }
    }

    /// <summary>مجموعة أعمدة أسبوع واحد في رسم إنتاج المنتجات</summary>
    public class ChartWeekGroup
    {
        public string WeekLabel { get; init; } = "";
        public string TotalText { get; init; } = "";
        public List<ChartBar> Bars { get; init; } = new();
    }

    /// <summary>عمود واحد في الرسم: منتج في أسبوع (اللون بيميز المنتج)</summary>
    public class ChartBar
    {
        public string Color { get; init; } = "#1F3864";
        public double Height { get; init; }
        public string Tooltip { get; init; } = "";
    }

    /// <summary>عنصر في مفتاح ألوان الرسم: المنتج ولونه وإجماليه في الفترة</summary>
    public class ChartLegendItem
    {
        public string Color { get; init; } = "#1F3864";
        public string ProductName { get; init; } = "";
        public string TotalText { get; init; } = "";
    }

    /// <summary>سطر واحد في كشف أجور الفترة (شهري) — مرتّب بالأجر</summary>
    public class PayrollRow
    {
        public int Rank { get; private init; }
        public string WorkerName { get; private init; } = "";
        public string TypeText { get; private init; } = "";
        public int DaysWorked { get; private init; }
        public decimal NetWorkdays { get; private init; }
        public string DailyWageText { get; private init; } = "";
        public string BonusText { get; private init; } = "";
        public string AdvanceText { get; private init; } = "";
        public string NetWageText { get; private init; } = "";

        public static PayrollRow From(WorkerPayrollDto dto, int rank) => new()
        {
            Rank = rank,
            WorkerName = dto.WorkerName,
            TypeText = dto.IsHourly ? "بالساعة" : "إنتاج",
            DaysWorked = dto.DaysWorked,
            NetWorkdays = dto.NetWorkdays,
            DailyWageText = dto.DailyWageEgp > 0 ? $"{dto.DailyWageEgp:N0} ج" : "لم يُحدد",
            BonusText = dto.BonusEgp > 0 ? $"{dto.BonusEgp:N0} ج" : "—",
            AdvanceText = dto.AdvanceEgp > 0 ? $"{dto.AdvanceEgp:N0} ج" : "—",
            NetWageText = dto.DailyWageEgp > 0 || dto.BonusEgp > 0 || dto.AdvanceEgp > 0 ? $"{dto.NetWageEgp:N0} ج" : "—"
        };
    }

    /// <summary>سطر واحد في كشف الأسبوع (مرتّب بصافي اليوميات)</summary>
    public class WeeklyReportRow
    {
        public int Rank { get; private init; }
        public string BestMark { get; private init; } = "";
        public string WorkerName { get; private init; } = "";
        public decimal ProducedWorkdays { get; private init; }
        public int TotalPieces { get; private init; }
        public int PresentDays { get; private init; }
        public int AbsentWithPermissionDays { get; private init; }
        public int AbsentWithoutPermissionDays { get; private init; }
        public decimal AbsenceDeduction { get; private init; }
        public decimal PenaltyDeduction { get; private init; }
        public decimal NetWorkdays { get; private init; }

        // ------- الأعمدة المدمجة (الكشف اتنضّف من 12 عمود لـ 9) -------

        /// <summary>
        /// الغياب كرقم واحد: "3 (منهم 1 بإذن)".
        ///
        /// كانوا عمودين (بإذن / بدون إذن). المدير بيسأل "غاب كام؟" الأول،
        /// والتفصيل بيهمه بعد كده — فالرقم الكبير قدام والتفصيل جنبه.
        /// </summary>
        public string AbsenceText
        {
            get
            {
                var total = AbsentWithPermissionDays + AbsentWithoutPermissionDays;
                if (total == 0) return "—";

                return AbsentWithPermissionDays == 0
                    ? total.ToString()
                    : $"{total} (منهم {AbsentWithPermissionDays} بإذن)";
            }
        }

        /// <summary>
        /// إجمالي الخصم من غياب وجزاءات.
        ///
        /// كانوا عمودين، والاتنين أصلاً مطروحين من الصافي اللي جنبهم —
        /// يعني نفس الأرقام معروضة مرتين. المدير محتاج يعرف "اتخصم منه
        /// كام" مش "اتخصم منه كام من نوعين"؛ التفصيل في تقرير العامل.
        /// </summary>
        public decimal TotalDeduction => AbsenceDeduction + PenaltyDeduction;

        public string DeductionText => TotalDeduction == 0 ? "—" : $"{TotalDeduction:0.##}";
        public string NetColor { get; private init; } = "#1F3864";
        /// <summary>أجر الأسبوع بالجنيه للعرض (فاضي لو مفيش سعر يومية)</summary>
        public string WageText { get; private init; } = "";
        public string BreakdownText { get; private init; } = "";
        public string PenaltiesText { get; private init; } = "";

        /// <summary>هل فيه تفاصيل تستحق العرض في لوحة التفاصيل؟</summary>
        public bool HasBreakdown => BreakdownText.Length > 0;
        public bool HasPenalties => PenaltiesText.Length > 0;

        public static WeeklyReportRow From(WorkerWeeklySummaryDto dto, int rank) => new()
        {
            Rank = rank,
            BestMark = dto.IsBestWorkerOfWeek ? "⭐" : "",
            WorkerName = dto.WorkerName,
            ProducedWorkdays = dto.ProducedWorkdays,
            TotalPieces = dto.TotalPieces,
            PresentDays = dto.PresentDays,
            AbsentWithPermissionDays = dto.AbsentWithPermissionDays,
            AbsentWithoutPermissionDays = dto.AbsentWithoutPermissionDays,
            AbsenceDeduction = dto.AbsenceDeduction,
            PenaltyDeduction = dto.PenaltyDeduction,
            NetWorkdays = dto.NetWorkdays,
            NetColor = dto.NetWorkdays < 0 ? "#C62828" : "#1F3864", // الصافي السالب أحمر
            WageText = dto.DailyWageEgp > 0 ? $"{dto.NetWageEgp:N0} ج" : "—",
            BreakdownText = string.Join("، ",
                dto.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount} قطعة ({b.Workdays} يومية)")),
            PenaltiesText = string.Join("، ",
                dto.Penalties.Select(p => $"{p.Date:MM/dd} {p.Reason} ({p.DeductionName})"))
        };
    }

    /// <summary>عنصر في قائمة اختيار العامل لتقرير عامل معيّن</summary>
    public class WorkerPickItem
    {
        public int Id { get; init; }
        public string Display { get; init; } = "";
    }

    /// <summary>سطر إنتاج مرحلة في التقارير (منتج/مرحلة + قطع + يوميات)</summary>
    public class GeneralStageRow
    {
        public string ProductName { get; private init; } = "";
        public string StageDisplay { get; private init; } = "";
        public int Pieces { get; private init; }
        public decimal Workdays { get; private init; }

        public static GeneralStageRow From(ProductStageProductionDto dto) => new()
        {
            ProductName = dto.ProductName,
            // آخر مرحلة = إنتاج مكتمل خرج من الخط — بنعلّمها للمستخدم
            StageDisplay = dto.IsLastStage ? $"{dto.StageName} ✅ (مكتمل)" : dto.StageName,
            Pieces = dto.Pieces,
            Workdays = dto.Workdays
        };
    }

    /// <summary>سطر عامل في التقرير العام (مرتّب باليوميات)</summary>
    public class GeneralWorkerRow
    {
        public int Rank { get; private init; }
        public string WorkerName { get; private init; } = "";
        public string TypeText { get; private init; } = "";
        public int TotalPieces { get; private init; }
        public decimal TotalWorkdays { get; private init; }

        public static GeneralWorkerRow From(WorkerProductionSummaryDto dto, int rank) => new()
        {
            Rank = rank,
            WorkerName = dto.WorkerName,
            TypeText = dto.IsHourly ? "بالساعة" : "إنتاج",
            TotalPieces = dto.TotalPieces,
            TotalWorkdays = dto.TotalWorkdays
        };
    }

    /// <summary>سطر يوم في تقرير عامل معيّن</summary>
    public class WorkerDayRow
    {
        public string DateText { get; private init; } = "";
        public int Pieces { get; private init; }
        public decimal Workdays { get; private init; }
        public string Detail { get; private init; } = "";

        public static WorkerDayRow From(WorkerDayProductionDto dto) => new()
        {
            DateText = dto.Date.ToString("yyyy/MM/dd (dddd)"),
            Pieces = dto.Pieces,
            Workdays = dto.Workdays,
            Detail = dto.Detail
        };
    }

    /// <summary>سطر جزاء في تقرير عامل معيّن</summary>
    public class WorkerPenaltyRow
    {
        public string DateText { get; private init; } = "";
        public string Reason { get; private init; } = "";
        public string DeductionName { get; private init; } = "";

        public static WorkerPenaltyRow From(PenaltySummaryDto dto) => new()
        {
            DateText = dto.Date.ToString("yyyy/MM/dd"),
            Reason = dto.Reason,
            DeductionName = dto.Deduction.ToArabicName()
        };
    }
}
