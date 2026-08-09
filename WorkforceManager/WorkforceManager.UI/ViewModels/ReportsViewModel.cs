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

        // ======================= محتاج تصرّف =======================
        // القايمة دي فوق كل حاجة عن قصد: الجدول بيقول حقيقة، وده
        // بيقول اعمل إيه. المدير بيفتح الشاشة عشان يعرف يتصرّف في إيه،
        // مش عشان يقرا متوسطات.

        public ObservableCollection<AttentionItem> Attention { get; } = new();

        public bool HasAttention => Attention.Count > 0;

        [ObservableProperty]
        private string _attentionSummary = "";

        private async Task LoadAttentionAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<NeedsAttentionService>();

            Attention.Clear();
            foreach (var item in await service.GetAsync(DailyDate)) Attention.Add(item);

            AttentionSummary = Attention.Count == 0
                ? "مفيش حاجة محتاجة تصرّف — كله تمام"
                : $"{Attention.Count} حاجة محتاجة تصرّف";

            OnPropertyChanged(nameof(HasAttention));
        }

        /// <summary>بيفتح شاشة العمال على العامل ده — الإجراء بيتم من مكانه</summary>
        [RelayCommand]
        private void OpenWorker(AttentionItem? item)
        {
            if (item?.WorkerId is null) return;

            Notify.Info(
                $"افتح شاشة \"العمال والمهارات\" ودوّر على \"{item.Title}\" — " +
                "من هناك تقدر تغيّر نجومه أو تظبّط سعر يوميته.",
                "الإجراء");
        }

        /// <summary>
        /// أول تحميل: اللي محتاج تصرّف الأول، وبعده تقييم اليوم وإنتاجه
        /// والرسم البياني.
        ///
        /// الكشوف والتقارير اللي كانت هنا اتنقلت لشاشة "التقارير" — دي
        /// شاشة بتتشاف وبيتصرّف منها، مش بتطلّع ورق.
        /// </summary>
        public async Task InitializeAsync()
        {
            await LoadAttentionAsync();
            await LoadDailyAsync();
            await LoadOutputAsync();
            await LoadChartAsync();
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

    }
}
