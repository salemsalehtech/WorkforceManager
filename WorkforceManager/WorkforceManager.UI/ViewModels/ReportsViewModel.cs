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
                    // المنتج التام = اللي خلص آخر مرحلة. جمع المراحل كان
                    // بيحسب القطعة الواحدة مرة لكل مرحلة (5,000 قطعة على
                    // 11 مرحلة كانت بتطلع 55,000)
                    Pieces = product.CompletedPieces,
                    StartedPieces = product.StartedPieces,
                    WorkerCount = product.WorkerIds.Count
                });

            OnPropertyChanged(nameof(HasDailyProducts));

            DailyTotalPiecesText = activity.Sum(p => p.CompletedPieces).ToString("N0");

            // الأكتر إنتاجًا = الأكتر **تام**. الخدمة بترجّعهم مرتبين كده.
            var top = activity.FirstOrDefault(p => p.CompletedPieces > 0);
            DailyTopProductText = top?.ProductName ?? "—";
            DailyTopProductPiecesText = top is null ? "" : $"{top.CompletedPieces:N0} قطعة";

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

        /// <summary>
        /// ألوان سلاسل المنتجات — ترتيب ثابت، بيتوزّع على المنتجات
        /// بالمعرّف مش بالترتيب.
        ///
        /// اللوحة القديمة كانت ألوان غامقة قوي (#1F3864 وإخواته): على خلفية
        /// بيضا نصها بيقرا رمادي، ومحدش بيقدر يفرّق بين المنتجات. اللوحة دي
        /// اتفحصت على خلفية فاتحة وغامقة: كل الألوان في نطاق الإضاءة
        /// المقروء، فوق حد التشبّع، وبتباين كافي مع الخلفية، وأقرب لونين
        /// متجاورين بيفضلوا مميزين لعمى الألوان.
        ///
        /// الترتيب نفسه مقصود: الأحمر والأخضر بعيدين عن بعض عشان أشهر
        /// أنواع عمى الألوان (بروتان/ديوتان) بتخلط بينهم.
        /// </summary>
        private static readonly string[] ChartPalette =
        {
            "#2563EB", "#D97706", "#0891B2", "#EF4444",
            "#7C3AED", "#16A34A", "#DB2777", "#B45309"
        };

        /// <summary>
        /// لون المنتجات اللي خرجت برّه اللوحة (التاسع فما فوق).
        ///
        /// توليد ألوان جديدة أو لفّ اللوحة من أولها كان بيدي لونين
        /// متطابقين لمنتجين مختلفين — والمستخدم مش هيعرف إن ده حصل.
        /// </summary>
        private const string OtherProductsColor = "#64748B";

        /// <summary>اسم مجموعة "الباقي" في المفتاح</summary>
        private const string OtherProductsLabel = "منتجات تانية";

        /// <summary>أقصى ارتفاع للعمود بالبكسل — الباقي بيتحسب نسبيًا عليه</summary>
        private const double MaxBarHeight = 190;

        /// <summary>الفاصل بين شرايح العمود الواحد — بيخلي الحدود تبان</summary>
        private const double SegmentGap = 2;

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

            // أول 8 منتجات بلون خاص، والباقي بيتجمّع في "منتجات تانية".
            // اللف على اللوحة من أولها كان بيدي لونين متطابقين لمنتجين
            // مختلفين، والمستخدم مش هيعرف إن ده حصل.
            var namedProducts = productTotals.Take(ChartPalette.Length).ToList();

            var colorByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Color: ChartPalette[i]))
                .ToDictionary(x => x.ProductId, x => x.Color);

            string ColorFor(int productId) =>
                colorByProduct.TryGetValue(productId, out var color) ? color : OtherProductsColor;

            // ترتيب الشرايح جوه العمود: نفس ترتيب المفتاح دايمًا، عشان
            // العين تلاقي المنتج في نفس المكان من أسبوع للتاني
            var orderByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Order: i))
                .ToDictionary(x => x.ProductId, x => x.Order);

            int OrderFor(int productId) =>
                orderByProduct.TryGetValue(productId, out var order) ? order : ChartPalette.Length;

            ChartLegend.Clear();
            foreach (var p in namedProducts)
            {
                ChartLegend.Add(new ChartLegendItem
                {
                    Color = ColorFor(p.ProductId),
                    ProductName = p.ProductName,
                    TotalText = $"{p.Total:N0} قطعة"
                });
            }

            var otherTotal = productTotals.Skip(ChartPalette.Length).Sum(p => p.Total);
            if (otherTotal > 0)
            {
                ChartLegend.Add(new ChartLegendItem
                {
                    Color = OtherProductsColor,
                    ProductName = $"{OtherProductsLabel} ({productTotals.Count - namedProducts.Count})",
                    TotalText = $"{otherTotal:N0} قطعة"
                });
            }

            // كل أسابيع الفترة بالترتيب الزمني (حتى الفاضية — محور الزمن لازم يكون متصل)
            var (firstWeekStart, _) = WeeklySummaryService.GetWorkWeekRange(DateTime.Today.AddDays(-7 * (SelectedChartWeeks - 1)));
            var pointsByWeek = points.ToLookup(p => p.WeekStart);

            // المقياس بقى على **إجمالي الأسبوع** مش على أعلى منتج: الأعمدة
            // بقت مكدّسة، فطول العمود بيمثّل إنتاج الأسبوع كله. المقياس
            // القديم (أعلى منتج) كان هيخلي الأعمدة تطلع برّه الرسمة.
            var weekTotals = points
                .GroupBy(p => p.WeekStart)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.CompletedPieces));

            var maxWeekTotal = weekTotals.Count == 0 ? 1 : weekTotals.Values.Max();

            ChartWeekGroups.Clear();
            for (var week = firstWeekStart; week <= DateTime.Today; week = week.AddDays(7))
            {
                var weekPoints = pointsByWeek[week]
                    .OrderBy(p => OrderFor(p.ProductId))
                    .ThenBy(p => p.ProductName)
                    .ToList();

                var weekTotal = weekPoints.Sum(p => p.CompletedPieces);
                var weekEnd = week.AddDays(6);

                ChartWeekGroups.Add(new ChartWeekGroup
                {
                    WeekLabel = $"{week:dd/MM}",
                    TotalText = weekTotal == 0 ? "" : $"{weekTotal:N0}",
                    HasWork = weekTotal > 0,
                    // الأسبوع الحالي بيتعلّم: المقارنة بيه ناقصة لأنه لسه مكملش
                    IsCurrentWeek = week == WeeklySummaryService.GetWorkWeekRange(DateTime.Today).WeekStart,
                    Segments = weekPoints.Select(p => new ChartBar
                    {
                        Color = ColorFor(p.ProductId),
                        // الارتفاع نسبي لإجمالي أعلى أسبوع. الفاصل بين
                        // الشرايح بيتخصم من الارتفاع عشان مجموع العمود
                        // يفضل مظبوط بصريًا
                        Height = Math.Max(3,
                            (double)p.CompletedPieces / maxWeekTotal * MaxBarHeight - SegmentGap),
                        Tooltip = $"{p.ProductName}\nأسبوع {week:dd/MM} → {weekEnd:dd/MM}\n" +
                                  $"{p.CompletedPieces:N0} قطعة مكتملة"
                    }).ToList()
                });
            }

            ChartHasData = points.Count > 0;
            RefreshChartTrend();
        }

        // ------- اتجاه الإنتاج -------

        [ObservableProperty]
        private string _chartTrendText = "";

        [ObservableProperty]
        private string _chartTrendColor = "#6B7686";

        [ObservableProperty]
        private bool _hasChartTrend;

        /// <summary>
        /// مقارنة آخر أسبوع **مكتمل** بالأسبوع اللي قبله.
        ///
        /// الأسبوع الحالي مستبعد عن قصد: لسه مكملش، فمقارنته بأسبوع كامل
        /// بتقول "الإنتاج نازل" كل يوم أحد — وده مش صحيح.
        /// </summary>
        private void RefreshChartTrend()
        {
            var completed = ChartWeekGroups.Where(w => !w.IsCurrentWeek && w.HasWork).ToList();

            HasChartTrend = completed.Count >= 2;
            if (!HasChartTrend)
            {
                ChartTrendText = "";
                return;
            }

            var last = ParseTotal(completed[^1].TotalText);
            var previous = ParseTotal(completed[^2].TotalText);

            if (previous == 0)
            {
                HasChartTrend = false;
                return;
            }

            var change = (double)(last - previous) / previous * 100;

            ChartTrendText = change switch
            {
                > 1 => $"آخر أسبوع مكتمل أعلى بـ {change:0}% عن اللي قبله",
                < -1 => $"آخر أسبوع مكتمل أقل بـ {Math.Abs(change):0}% عن اللي قبله",
                _ => "آخر أسبوعين مكتملين تقريبًا زي بعض"
            };

            ChartTrendColor = change switch
            {
                > 1 => "#16A34A",
                < -1 => "#EF4444",
                _ => "#6B7686"
            };
        }

        private static int ParseTotal(string text) =>
            int.TryParse(text.Replace(",", ""), out var value) ? value : 0;

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

        // ------- أرقام الملخص -------
        // كانت سطرين طويلين مفصولين بـ "|" — رقم مهم زي الأجر النهائي كان
        // بيضيع وسط ٦ أرقام تانية. بقت مربعات، كل رقم في مكانه.

        /// <summary>قطع أنتجها العامل في الفترة</summary>
        [ObservableProperty]
        private string _workerPiecesText = "0";

        /// <summary>يوميات منتجة (قبل الخصومات)</summary>
        [ObservableProperty]
        private string _workerWorkdaysText = "0";

        /// <summary>صافي اليوميات بعد الغياب والجزاءات</summary>
        [ObservableProperty]
        private string _workerNetWorkdaysText = "";

        /// <summary>أيام الحضور</summary>
        [ObservableProperty]
        private string _workerPresentDaysText = "0";

        /// <summary>تفصيل الغياب تحت رقم الحضور</summary>
        [ObservableProperty]
        private string _workerAbsenceText = "";

        /// <summary>الأجر النهائي بالجنيه — الرقم اللي بيتصرف فعلاً</summary>
        [ObservableProperty]
        private string _workerNetWageText = "—";

        /// <summary>معادلة الأجر: يوميات × سعر + حوافز − سلف</summary>
        [ObservableProperty]
        private string _workerWageBreakdownText = "";

        /// <summary>هل فيه تقرير معروض؟ (يتحكم في ظهور الأرقام)</summary>
        [ObservableProperty]
        private bool _hasWorkerReport;

        public ObservableCollection<GeneralStageRow> WorkerByProductStage { get; } = new();
        public ObservableCollection<WorkerDayRow> WorkerByDay { get; } = new();
        public ObservableCollection<WorkerPenaltyRow> WorkerPenalties { get; } = new();

        /// <summary>تقييم العامل على المنتجات — الأعلى الأول</summary>
        public ObservableCollection<WorkerSkillSummaryDto> WorkerSkills { get; } = new();

        [ObservableProperty]
        private bool _hasWorkerSkills;

        /// <summary>مفيش سعر يومية — أجره هيطلع صفر مهما أنتج</summary>
        [ObservableProperty]
        private bool _workerHasNoWageRate;

        /// <summary>
        /// السلف أكلت الأجر والصافي بالسالب.
        ///
        /// الرقم بيتعرض زي ما هو مع تحذير بدل ما يتصفّر: "العامل مدين"
        /// حقيقة محاسبية لازم تتشاف، وتصفيرها بتخفي إن السلف اتصرفت.
        /// </summary>
        [ObservableProperty]
        private bool _workerIsWageNegative;

        [ObservableProperty]
        private string _workerNegativeWageText = "";

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

            // المهارات: "أنتج كام" لوحده مبيقولش هو شاطر في إيه
            WorkerSkills.Clear();
            foreach (var s in report.Skills) WorkerSkills.Add(s);
            HasWorkerSkills = WorkerSkills.Count > 0;

            // تحذيرات بتظهر بس لما تبقى موجودة فعلاً
            WorkerHasNoWageRate = report.HasNoWageRate;
            WorkerIsWageNegative = report.IsWageNegative;
            WorkerNegativeWageText = report.IsWageNegative
                ? $"السلف ({report.AdvanceEgp:N0} ج) أكبر من أجره — الصافي {report.NetWageEgp:N0} ج، " +
                  "يعني العامل مدين بالفرق"
                : "";

            var days = (WorkerTo.Date - WorkerFrom.Date).Days + 1;
            WorkerReportHeader =
                $"{report.WorkerName} — {report.TypeText}   |   " +
                $"من {report.From:yyyy/MM/dd} إلى {report.To:yyyy/MM/dd} ({days} يوم)";
            // القطع هنا بتتجمع على كل المراحل عن قصد: دي قياس شغل العامل
            // نفسه مش إنتاج المنتج، فالقطعة اللي عدّت على مرحلتين شغل مرتين.
            WorkerPiecesText = report.TotalPieces.ToString("N0");
            WorkerWorkdaysText = report.ProducedWorkdays.ToString("0.##");
            WorkerNetWorkdaysText = report.NetWorkdays != report.ProducedWorkdays
                ? $"الصافي بعد الخصومات: {report.NetWorkdays:0.##}"
                : "مفيش خصومات";

            WorkerPresentDaysText = report.PresentDays.ToString();
            var absences = new List<string>();
            if (report.AbsentWithPermissionDays > 0)
                absences.Add($"غياب بإذن {report.AbsentWithPermissionDays}");
            if (report.AbsentWithoutPermissionDays > 0)
                absences.Add($"بدون إذن {report.AbsentWithoutPermissionDays} (خصم {report.AbsenceDeduction:0.##})");
            WorkerAbsenceText = absences.Count > 0 ? string.Join(" · ", absences) : "مفيش غياب";

            // معادلة الأجر كاملة: يوميات × سعر + حوافز − سلف
            WorkerNetWageText = report.DailyWageEgp > 0 ? $"{report.NetWageEgp:N0} ج" : "—";
            var adjParts = "";
            if (report.BonusEgp > 0) adjParts += $" + حوافز {report.BonusEgp:N0}";
            if (report.AdvanceEgp > 0) adjParts += $" − سلف {report.AdvanceEgp:N0}";
            if (report.PenaltyDeduction > 0) adjParts += $" (خصم جزاءات {report.PenaltyDeduction:0.##} يومية)";
            WorkerWageBreakdownText = report.DailyWageEgp > 0
                ? $"{report.NetWorkdays:0.##} × {report.DailyWageEgp:N0}{adjParts}"
                : $"صافي اليوميات {report.NetWorkdays:0.##}{adjParts}";
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
}
