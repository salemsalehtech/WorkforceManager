using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "التقييم والمتابعة"، وفيها تبويبين:
    ///
    /// 1) إنتاج اليوم: خلص كام ودخل كام واتهلك كام، لكل منتج وللمصنع كله.
    /// 2) رسم إنتاج المنتجات: نفس الأرقام على مدى، مقسومة بيوم أو أسبوع
    ///    أو شهر، مع الهالك والمتوسط والمقارنة بالفترة اللي قبلها.
    ///
    /// **تبويب "تقييم اليوم" اتشال بالكامل.** كان بيعرض كروت بتكرر
    /// أرقام تبويب إنتاج اليوم بصياغة تانية، وجدول بيقارن كل عامل
    /// بمتوسط زمايله. الجدول ده موجود في مُنشئ التقارير (الإنتاج
    /// بالعامل) وبيتصدّر Excel كمان، فوجوده هنا كان تكرار — والتكرار
    /// هو اللي كان بيخلي رقمين مختلفين شكلًا لنفس اليوم على شاشتين.
    /// </summary>
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ReportsViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;

            // الحقل مباشرة مش الخاصية: الخاصية بتنادي RefreshRangeOptions
            // وتطلب تحميل، والشاشة لسه مش جاهزة
            _selectedGrain = Grains[1];
            RefreshRangeOptions();
        }

        public async Task InitializeAsync()
        {
            await LoadOutputAsync();
            await LoadChartAsync();
            await LoadWorkerAveragesAsync();
        }

        // ======================= تبويب إنتاج اليوم =======================

        [ObservableProperty]
        private DateTime _outputDate = DateTime.Today;

        partial void OnOutputDateChanged(DateTime value) => SafeAsync.Run(LoadOutputAsync);

        /// <summary>يوم/أسبوع/شهر — أي فترة يعرضها التبويب حاليًا. الأسبوع والشهر بيتحددوا من OutputDate (أي يوم جواهم)</summary>
        [ObservableProperty]
        private ChartGrain _outputGrain = ChartGrain.Day;

        partial void OnOutputGrainChanged(ChartGrain value)
        {
            OnPropertyChanged(nameof(IsOutputGrainDay));
            OnPropertyChanged(nameof(IsOutputGrainWeek));
            OnPropertyChanged(nameof(IsOutputGrainMonth));
            SafeAsync.Run(LoadOutputAsync);
        }

        public bool IsOutputGrainDay => OutputGrain == ChartGrain.Day;
        public bool IsOutputGrainWeek => OutputGrain == ChartGrain.Week;
        public bool IsOutputGrainMonth => OutputGrain == ChartGrain.Month;

        [RelayCommand]
        private void SetOutputGrain(string? key) => OutputGrain = key switch
        {
            "week" => ChartGrain.Week,
            "month" => ChartGrain.Month,
            _ => ChartGrain.Day
        };

        /// <summary>وصف الفترة المعروضة ("الأسبوع من 17/08 إلى 23/08" أو "شهر 08/2026") — فاضي في وضع اليوم</summary>
        [ObservableProperty]
        private string _outputPeriodLabel = "";

        /// <summary>منتجات فيها حركة في الفترة المعروضة (خلص منها أو دخل الخط)</summary>
        public ObservableCollection<DailyProductReportDto> OutputProducts { get; } = new();

        [ObservableProperty]
        private string _outputCompletedText = "0";

        [ObservableProperty]
        private string _outputStartedText = "0";

        /// <summary>هالك اليوم — على كل المراحل، مش آخر مرحلة بس</summary>
        [ObservableProperty]
        private string _outputScrapText = "0";

        /// <summary>نسبة الهالك لليوم — رقم لوحده مبيقولش هو كتير ولا لأ</summary>
        [ObservableProperty]
        private string _outputScrapRateText = "";

        [ObservableProperty]
        private bool _outputHasScrap;

        [ObservableProperty]
        private bool _outputIsClosed;

        [ObservableProperty]
        private bool _outputIsEmpty = true;

        /// <summary>عمال إنتاجهم اليوم ده قلّ بشكل ملحوظ عن متوسط آخر أيام شغلهم (ProductionTrendService)</summary>
        public ObservableCollection<ProductionDeclineDto> DecliningWorkers { get; } = new();

        [ObservableProperty]
        private bool _hasDecliningWorkers;

        /// <summary>
        /// تقرير الإنتاج للفترة المعروضة (يوم/أسبوع/شهر حسب OutputGrain):
        /// كام قطعة خلصت آخر مرحلة (= منتج تام)، وكام قطعة دخلت أول
        /// مرحلة، وكام هالك اتسجّل. الأرقام محسوبة من السجلات نفسها.
        /// </summary>
        private async Task LoadOutputAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DailyProductionReportService>();
            var scrapService = scope.ServiceProvider.GetRequiredService<ScrapService>();
            var trendService = scope.ServiceProvider.GetRequiredService<ProductionTrendService>();

            DailyProductionReportDto report;
            IReadOnlyList<ScrapRecordDto> scrap;

            switch (OutputGrain)
            {
                case ChartGrain.Week:
                    var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(OutputDate);
                    report = await service.GetForRangeAsync(weekStart, weekEnd);
                    scrap = await scrapService.GetByRangeAsync(weekStart, weekEnd);
                    OutputPeriodLabel = $"الأسبوع من {weekStart:dd/MM} إلى {weekEnd:dd/MM}";
                    break;

                case ChartGrain.Month:
                    var monthStart = new DateTime(OutputDate.Year, OutputDate.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    report = await service.GetForRangeAsync(monthStart, monthEnd);
                    scrap = await scrapService.GetByRangeAsync(monthStart, monthEnd);
                    OutputPeriodLabel = $"شهر {monthStart:MM/yyyy}";
                    break;

                default:
                    report = await service.GetAsync(OutputDate);
                    scrap = await scrapService.GetByDateAsync(OutputDate);
                    OutputPeriodLabel = "";
                    break;
            }

            // قايمة "قلّ عن المعتاد" مفهومها "النهارده" دايمًا — مش
            // بتتبع الفترة المعروضة هنا (تصفح أسبوع فات معناهاش نتنبه
            // على تراجع في وقت غير النهارده)
            var declining = await trendService.GetDecliningWorkersAsync(DateTime.Today);
            DecliningWorkers.Clear();
            foreach (var worker in declining) DecliningWorkers.Add(worker);
            HasDecliningWorkers = DecliningWorkers.Count > 0;

            OutputProducts.Clear();
            foreach (var product in report.Products) OutputProducts.Add(product);

            OutputCompletedText = report.TotalCompletedPieces.ToString("N0");
            OutputStartedText = report.TotalStartedPieces.ToString("N0");
            OutputIsClosed = report.IsClosed;
            OutputIsEmpty = report.Products.Count == 0;

            var scrapTotal = scrap.Sum(s => s.PieceCount);
            OutputScrapText = scrapTotal.ToString("N0");
            OutputHasScrap = scrapTotal > 0;

            // النسبة على التام + الهالك: "كام من اللي اشتغلناه ضاع".
            // النسبة على التام لوحده كانت بتطلع أكبر من 100% في يوم
            // إنتاجه كله اتهلك.
            // "0% من شغل اليوم" تحت "0 قطعة هالك" بتقول نفس الحاجة مرتين
            var baseline = report.TotalCompletedPieces + scrapTotal;
            OutputScrapRateText = scrapTotal == 0 || baseline == 0
                ? ""
                : $"{(double)scrapTotal / baseline * 100:0.#}% من شغل اليوم";
        }

        // ======================= تبويب رسم إنتاج المنتجات =======================

        /// <summary>
        /// التقسيم الزمني. الافتراضي أسبوع: اليوم بيبقى ضوضاء على مدى
        /// طويل، والشهر بيخبّي التفاصيل — الأسبوع هو وحدة الشغل هنا
        /// أصلاً (خميس → أربع).
        /// </summary>
        public IReadOnlyList<GrainOption> Grains { get; } = new[]
        {
            new GrainOption(ChartGrain.Day, "بيوم"),
            new GrainOption(ChartGrain.Week, "بأسبوع"),
            new GrainOption(ChartGrain.Month, "بشهر")
        };

        [ObservableProperty]
        private GrainOption _selectedGrain;

        partial void OnSelectedGrainChanged(GrainOption value)
        {
            // كل تقسيم وعدد فتراته المعقول: 30 يوم يبقوا 30 عمود، بس
            // 30 شهر يبقوا سنتين ونص — فالخيارات بتتغيّر مع التقسيم
            RefreshRangeOptions();

            OnPropertyChanged(nameof(IsGrainDay));
            OnPropertyChanged(nameof(IsGrainWeek));
            OnPropertyChanged(nameof(IsGrainMonth));

            SafeAsync.Run(LoadChartAsync);
        }

        public bool IsGrainDay => SelectedGrain.Grain == ChartGrain.Day;
        public bool IsGrainWeek => SelectedGrain.Grain == ChartGrain.Week;
        public bool IsGrainMonth => SelectedGrain.Grain == ChartGrain.Month;

        [RelayCommand]
        private void SetGrain(string? key)
        {
            var grain = key switch
            {
                "day" => ChartGrain.Day,
                "month" => ChartGrain.Month,
                _ => ChartGrain.Week
            };

            SelectedGrain = Grains.First(g => g.Grain == grain);
        }

        public ObservableCollection<RangeOption> RangeOptions { get; } = new();

        [ObservableProperty]
        private RangeOption? _selectedRange;

        partial void OnSelectedRangeChanged(RangeOption? value)
        {
            if (!_suppressReload) SafeAsync.Run(LoadChartAsync);
        }

        private bool _suppressReload;

        private void RefreshRangeOptions()
        {
            var counts = SelectedGrain.Grain switch
            {
                ChartGrain.Day => new[] { 7, 14, 30, 60 },
                ChartGrain.Week => new[] { 4, 8, 12, 24 },
                _ => new[] { 3, 6, 12, 24 }
            };

            var unit = SelectedGrain.Grain switch
            {
                ChartGrain.Day => "يوم",
                ChartGrain.Week => "أسبوع",
                _ => "شهر"
            };

            // العلم بيمنع تغيير الاختيار إنه يطلب تحميل تاني — التقسيم
            // بيحمّل مرة واحدة في الآخر
            _suppressReload = true;
            try
            {
                RangeOptions.Clear();
                foreach (var count in counts)
                    RangeOptions.Add(new RangeOption(count, $"آخر {count} {unit}"));

                SelectedRange = RangeOptions[1];
            }
            finally
            {
                _suppressReload = false;
            }
        }

        /// <summary>
        /// فلتر المنتجات: مفيش علامة على حاجة = اعرض الكل. المستخدم
        /// اللي بيشيل كل العلامات قصده يشوف الكل تاني، مش يشوف رسم فاضي.
        /// </summary>
        public ObservableCollection<ChartProductFilterItem> ChartProducts { get; } = new();

        [ObservableProperty]
        private bool _isProductMenuOpen;

        public bool HasProductFilter => ChartProducts.Any(p => !p.IsChecked);

        public string ProductFilterText
        {
            get
            {
                var shown = ChartProducts.Count(p => p.IsChecked);
                return shown == ChartProducts.Count ? "كل المنتجات" : $"{shown} من {ChartProducts.Count}";
            }
        }

        private void OnProductFilterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ChartProductFilterItem.IsChecked)) return;

            OnPropertyChanged(nameof(HasProductFilter));
            OnPropertyChanged(nameof(ProductFilterText));
            RebuildChart();
        }

        /// <summary>أعمدة الرسم بالترتيب الزمني</summary>
        public ObservableCollection<ChartBucket> ChartBuckets { get; } = new();

        /// <summary>مفتاح الألوان: منتج → لون + إجمالي الفترة + تغيّره</summary>
        public ObservableCollection<ChartLegendItem> ChartLegend { get; } = new();

        [ObservableProperty]
        private bool _chartHasData;

        [ObservableProperty]
        private string _chartTotalText = "0";

        /// <summary>الهالك في الفترة كلها + نسبته</summary>
        [ObservableProperty]
        private string _chartScrapText = "0";

        [ObservableProperty]
        private string _chartScrapRateText = "";

        [ObservableProperty]
        private string _chartAverageText = "";

        [ObservableProperty]
        private string _chartHint = "";

        // ------- المقارنة بالفترة اللي فاتت -------

        [ObservableProperty]
        private string _chartTrendText = "";

        /// <summary>مفتاح فرشاة من اللوحة — مش كود لون (شوف <see cref="ThemeBrush"/>)</summary>
        [ObservableProperty]
        private string _chartTrendColor = "InkSoftBrush";

        [ObservableProperty]
        private bool _hasChartTrend;

        /// <summary>
        /// سلاسل المنتجات — ترتيب ثابت، بيتوزّع على المنتجات بالمعرّف
        /// مش بالترتيب، فالمنتج بياخد نفس اللون مهما اتغيّر الفلتر.
        ///
        /// **دي مفاتيح فُرَش مش أكواد ألوان** (شوف <see cref="ThemeBrush"/>).
        /// </summary>
        private static readonly string[] ChartPalette =
        {
            "Series1Brush", "Series2Brush", "Series3Brush", "Series4Brush",
            "Series5Brush", "Series6Brush", "Series7Brush", "Series8Brush"
        };

        /// <summary>
        /// لون المنتجات اللي خرجت برّه اللوحة (التاسع فما فوق).
        ///
        /// توليد ألوان جديدة أو لفّ اللوحة من أولها كان بيدي لونين
        /// متطابقين لمنتجين مختلفين — والمستخدم مش هيعرف إن ده حصل.
        /// </summary>
        private const string OtherProductsColor = "SeriesOtherBrush";

        private const string OtherProductsLabel = "منتجات تانية";

        /// <summary>لون الهالك — مميز عن ألوان المنتجات عن قصد</summary>
        private const string ScrapColor = "DangerBrush";

        /// <summary>
        /// أقصى ارتفاع للعمود بالبكسل — الباقي بيتحسب نسبيًا عليه.
        /// لازم يفضل أقل من ارتفاع منطقة الرسم في XAML بفرق بسيط.
        /// </summary>
        private const double MaxBarHeight = 260;

        /// <summary>الفاصل بين شرايح العمود الواحد — بيخلي الحدود تبان</summary>
        private const double SegmentGap = 2;

        /// <summary>نقاط الفترة المعروضة — الفلتر بيشتغل عليها من غير استعلام تاني</summary>
        private List<ProductOutputPointDto> _points = new();

        /// <summary>إجمالي الفترة اللي قبل المعروضة — للمقارنة</summary>
        private int _previousPeriodTotal;

        private Dictionary<int, int> _previousByProduct = new();

        private async Task LoadChartAsync()
        {
            if (SelectedRange is null) return;

            var count = SelectedRange.Count;
            var grain = SelectedGrain.Grain;

            var from = ProductionChartService.StartOfLast(count, grain);
            var to = DateTime.Today;

            // الفترة اللي قبلها بنفس الطول بالظبط — المقارنة لازم تبقى
            // زي بزي، مش شهر مقابل أسبوعين
            var previousTo = from.AddDays(-1);
            var previousFrom = ProductionChartService.BucketOf(previousTo, grain).Start;
            for (var i = 1; i < count; i++)
                previousFrom = grain switch
                {
                    ChartGrain.Day => previousFrom.AddDays(-1),
                    ChartGrain.Week => previousFrom.AddDays(-7),
                    _ => previousFrom.AddMonths(-1)
                };

            List<ProductOutputPointDto> previous;

            using (var scope = _scopeFactory.CreateScope())
            {
                var chartService = scope.ServiceProvider.GetRequiredService<ProductionChartService>();
                _points = await chartService.GetProductOutputAsync(from, to, grain);
                previous = await chartService.GetProductOutputAsync(previousFrom, previousTo, grain);
            }

            _previousPeriodTotal = previous.Sum(p => p.CompletedPieces);
            _previousByProduct = previous
                .GroupBy(p => p.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.CompletedPieces));

            RefreshProductFilter();
            RebuildChart();
        }

        /// <summary>
        /// بيحدّث قايمة الفلتر مع الفترة الجديدة، وبيحافظ على اللي
        /// المستخدم شاله — منتج شيلته من الرسم مايرجعش لوحده لما تغيّر
        /// المدة.
        /// </summary>
        private void RefreshProductFilter()
        {
            var unchecked_ = ChartProducts.Where(p => !p.IsChecked).Select(p => p.Id).ToHashSet();

            foreach (var item in ChartProducts) item.PropertyChanged -= OnProductFilterChanged;
            ChartProducts.Clear();

            foreach (var product in _points
                         .GroupBy(p => (p.ProductId, p.ProductName))
                         .Select(g => (g.Key.ProductId, g.Key.ProductName, Total: g.Sum(x => x.CompletedPieces)))
                         .OrderByDescending(x => x.Total)
                         .ThenBy(x => x.ProductName))
            {
                var item = new ChartProductFilterItem
                {
                    Id = product.ProductId,
                    Name = product.ProductName,
                    IsChecked = !unchecked_.Contains(product.ProductId)
                };

                item.PropertyChanged += OnProductFilterChanged;
                ChartProducts.Add(item);
            }

            OnPropertyChanged(nameof(HasProductFilter));
            OnPropertyChanged(nameof(ProductFilterText));
        }

        /// <summary>
        /// بيبني الرسم من النقاط المحمّلة. منفصل عن التحميل عشان الفلتر
        /// يشتغل من غير استعلام جديد على قاعدة البيانات.
        /// </summary>
        private void RebuildChart()
        {
            var grain = SelectedGrain.Grain;
            var count = SelectedRange?.Count ?? 8;

            var visible = ChartProducts.Where(p => p.IsChecked).Select(p => p.Id).ToHashSet();

            // مفيش علامة على حاجة = اعرض الكل (الفلتر مقفول مش "طابق ولا حاجة")
            var points = visible.Count == 0
                ? _points
                : _points.Where(p => visible.Contains(p.ProductId)).ToList();

            // المنتجات مرتبة بالأكتر إنتاجًا، ولون ثابت لكل منتج
            var productTotals = points
                .GroupBy(p => (p.ProductId, p.ProductName))
                .Select(g => (g.Key.ProductId, g.Key.ProductName, Total: g.Sum(x => x.CompletedPieces)))
                .OrderByDescending(x => x.Total)
                .ToList();

            var namedProducts = productTotals.Take(ChartPalette.Length).ToList();

            var colorByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Color: ChartPalette[i]))
                .ToDictionary(x => x.ProductId, x => x.Color);

            string ColorFor(int productId) =>
                colorByProduct.TryGetValue(productId, out var color) ? color : OtherProductsColor;

            // ترتيب الشرايح جوه العمود: نفس ترتيب المفتاح دايمًا، عشان
            // العين تلاقي المنتج في نفس المكان من فترة للتانية
            var orderByProduct = namedProducts
                .Select((p, i) => (p.ProductId, Order: i))
                .ToDictionary(x => x.ProductId, x => x.Order);

            int OrderFor(int productId) =>
                orderByProduct.TryGetValue(productId, out var order) ? order : ChartPalette.Length;

            BuildLegend(productTotals, namedProducts, ColorFor);

            // كل فترات المدى بالترتيب الزمني (حتى الفاضية — محور الزمن
            // لازم يكون متصل)
            var firstBucket = ProductionChartService.StartOfLast(count, grain);
            var pointsByBucket = points.ToLookup(p => p.BucketStart);
            var currentBucket = ProductionChartService.BucketOf(DateTime.Today, grain).Start;

            // المقياس على **إجمالي الفترة + هالكها**: العمود بقى بيحمل
            // الاتنين، فلو المقياس على التام لوحده الهالك بيطلع برّه
            var bucketTotals = points
                .GroupBy(p => p.BucketStart)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.CompletedPieces) + g.Sum(p => p.ScrapPieces));

            var maxBucketTotal = bucketTotals.Count == 0 ? 1 : Math.Max(1, bucketTotals.Values.Max());

            // المتوسط على الفترات اللي فيها شغل بس: الفترات الفاضية
            // بتنزّل المتوسط لرقم مالوش معنى (أجازات ويوم الجمعة)
            var workedTotals = points
                .GroupBy(p => p.BucketStart)
                .Select(g => g.Sum(p => p.CompletedPieces))
                .Where(t => t > 0)
                .ToList();

            var average = workedTotals.Count == 0 ? 0 : workedTotals.Average();
            var averageOffset = average / maxBucketTotal * MaxBarHeight;
            var showAverage = workedTotals.Count >= 2;

            ChartBuckets.Clear();

            for (var bucket = firstBucket;
                 bucket <= DateTime.Today;
                 bucket = ProductionChartService.NextBucket(bucket, grain))
            {
                var bucketPoints = pointsByBucket[bucket]
                    .OrderBy(p => OrderFor(p.ProductId))
                    .ThenBy(p => p.ProductName)
                    .ToList();

                var completed = bucketPoints.Sum(p => p.CompletedPieces);
                var scrapped = bucketPoints.Sum(p => p.ScrapPieces);
                var end = ProductionChartService.BucketOf(bucket, grain).End;

                double HeightOf(int pieces) =>
                    pieces <= 0 ? 0 : Math.Max(3, (double)pieces / maxBucketTotal * MaxBarHeight - SegmentGap);

                var segments = bucketPoints
                    .Where(p => p.CompletedPieces > 0)
                    .Select(p => new ChartBar
                    {
                        Color = ColorFor(p.ProductId),
                        Height = HeightOf(p.CompletedPieces),
                        Tooltip = $"{p.ProductName}\n{LabelFor(bucket, end, grain)}\n" +
                                  $"{p.CompletedPieces:N0} قطعة مكتملة"
                    })
                    .ToList();

                // الهالك فوق العمود: بيبان كزيادة على الشغل، مش جزء منه
                if (scrapped > 0)
                    segments.Insert(0, new ChartBar
                    {
                        Color = ScrapColor,
                        Height = HeightOf(scrapped),
                        Tooltip = $"هالك\n{LabelFor(bucket, end, grain)}\n{scrapped:N0} قطعة"
                    });

                ChartBuckets.Add(new ChartBucket
                {
                    Label = ShortLabel(bucket, grain),
                    Total = completed,
                    TotalText = completed == 0 ? "" : $"{completed:N0}",
                    HasWork = completed > 0 || scrapped > 0,
                    IsCurrent = bucket == currentBucket,
                    AverageOffset = averageOffset,
                    ShowAverage = showAverage,
                    Segments = segments
                });
            }

            var totalCompleted = points.Sum(p => p.CompletedPieces);
            var totalScrap = points.Sum(p => p.ScrapPieces);

            ChartTotalText = $"{totalCompleted:N0}";
            ChartScrapText = $"{totalScrap:N0}";

            // زي شاشة اليوم: "0%" جنب "0 قطعة" بتقول نفس الحاجة مرتين
            var baseline = totalCompleted + totalScrap;
            ChartScrapRateText = totalScrap == 0 || baseline == 0
                ? ""
                : $"{(double)totalScrap / baseline * 100:0.#}% من الشغل";

            ChartAverageText = showAverage
                ? $"متوسط {UnitName(grain)}: {average:N0} قطعة"
                : "";

            ChartHint = grain switch
            {
                ChartGrain.Day => "القطع المكتملة فقط: المسجلة على آخر مرحلة لكل منتج. الهالك محسوب على كل المراحل.",
                ChartGrain.Week => "الأسبوع خميس → أربع. القطع المكتملة فقط، والهالك على كل المراحل.",
                _ => "الشهر بالتقويم. القطع المكتملة فقط، والهالك على كل المراحل."
            };

            ChartHasData = points.Count > 0;
            RefreshTrend(totalCompleted, grain);
        }

        private void BuildLegend(
            List<(int ProductId, string ProductName, int Total)> productTotals,
            List<(int ProductId, string ProductName, int Total)> namedProducts,
            Func<int, string> colorFor)
        {
            ChartLegend.Clear();

            foreach (var p in namedProducts)
            {
                var (changeText, changeColor) = DescribeChange(
                    p.Total,
                    _previousByProduct.TryGetValue(p.ProductId, out var before) ? before : 0);

                ChartLegend.Add(new ChartLegendItem
                {
                    Color = colorFor(p.ProductId),
                    ProductName = p.ProductName,
                    TotalText = $"{p.Total:N0} قطعة",
                    ChangeText = changeText,
                    ChangeColor = changeColor
                });
            }

            var otherTotal = productTotals.Skip(ChartPalette.Length).Sum(p => p.Total);
            if (otherTotal > 0)
                ChartLegend.Add(new ChartLegendItem
                {
                    Color = OtherProductsColor,
                    ProductName = $"{OtherProductsLabel} ({productTotals.Count - namedProducts.Count})",
                    TotalText = $"{otherTotal:N0} قطعة"
                });
        }

        /// <summary>
        /// نسبة التغيّر عن نفس الرقم في الفترة اللي قبلها. بترجع فاضي
        /// لو مفيش أساس للمقارنة — "زاد ∞%" مش معلومة.
        /// </summary>
        private static (string Text, string Color) DescribeChange(int now, int before)
        {
            if (before <= 0) return ("", "InkSoftBrush");

            var change = (double)(now - before) / before * 100;

            return change switch
            {
                > 1 => ($"▲ {change:0}%", "GoodBrush"),
                < -1 => ($"▼ {Math.Abs(change):0}%", "DangerBrush"),
                _ => ("= زي الفترة اللي فاتت", "InkSoftBrush")
            };
        }

        /// <summary>
        /// مقارنة الفترة المعروضة كلها بالفترة اللي قبلها بنفس الطول.
        ///
        /// كانت بتقارن آخر أسبوعين مكتملين بس، فتغيير المدة مكانش بيغيّر
        /// الجملة — والمستخدم اللي بيبص على 24 أسبوع كان بياخد خلاصة
        /// عن أسبوعين منهم.
        /// </summary>
        private void RefreshTrend(int total, ChartGrain grain)
        {
            HasChartTrend = _previousPeriodTotal > 0;

            if (!HasChartTrend)
            {
                ChartTrendText = "";
                return;
            }

            var change = (double)(total - _previousPeriodTotal) / _previousPeriodTotal * 100;
            var unit = SelectedRange?.Display ?? "الفترة";

            ChartTrendText = change switch
            {
                > 1 => $"{unit}: {total:N0} قطعة — أعلى بـ {change:0}% عن الفترة اللي قبلها ({_previousPeriodTotal:N0})",
                < -1 => $"{unit}: {total:N0} قطعة — أقل بـ {Math.Abs(change):0}% عن الفترة اللي قبلها ({_previousPeriodTotal:N0})",
                _ => $"{unit}: {total:N0} قطعة — تقريبًا زي الفترة اللي قبلها ({_previousPeriodTotal:N0})"
            };

            ChartTrendColor = change switch
            {
                > 1 => "GoodBrush",
                < -1 => "DangerBrush",
                _ => "InkSoftBrush"
            };
        }

        private static string UnitName(ChartGrain grain) => grain switch
        {
            ChartGrain.Day => "اليوم",
            ChartGrain.Week => "الأسبوع",
            _ => "الشهر"
        };

        /// <summary>عنوان قصير تحت العمود — لازم يفضل مقروء وهو 60 عمود</summary>
        private static string ShortLabel(DateTime bucket, ChartGrain grain) => grain switch
        {
            ChartGrain.Month => $"{bucket:MM/yyyy}",
            _ => $"{bucket:dd/MM}"
        };

        /// <summary>الوصف الكامل في التلميح</summary>
        private static string LabelFor(DateTime start, DateTime end, ChartGrain grain) => grain switch
        {
            ChartGrain.Day => $"يوم {start:yyyy/MM/dd}",
            ChartGrain.Week => $"أسبوع {start:dd/MM} → {end:dd/MM}",
            _ => $"شهر {start:MM/yyyy}"
        };

        // ======================= تبويب متوسط إنتاج العمال =======================

        /// <summary>كل عامل عنده تاريخ إنتاج كافي (7 أيام شغل فعلية) ومتوسطه اليومي — مرتبين حسب SortDescending</summary>
        public ObservableCollection<WorkerProductionAverageDto> WorkerAverages { get; } = new();

        [ObservableProperty]
        private bool _hasWorkerAverages;

        /// <summary>الأعلى إنتاجًا فوق (true، الافتراضي) أو الأقل فوق (false)</summary>
        [ObservableProperty]
        private bool _sortDescending = true;

        private List<WorkerProductionAverageDto> _allWorkerAverages = new();

        [RelayCommand]
        private void ToggleSort()
        {
            SortDescending = !SortDescending;
            ApplyWorkerAveragesSort();
        }

        private void ApplyWorkerAveragesSort()
        {
            var sorted = SortDescending
                ? _allWorkerAverages.OrderByDescending(w => w.TrailingAverage).ToList()
                : _allWorkerAverages.OrderBy(w => w.TrailingAverage).ToList();

            WorkerAverages.Clear();
            foreach (var worker in sorted) WorkerAverages.Add(worker);
        }

        /// <summary>
        /// متوسط إنتاج كل عامل عنده تاريخ كافي — نفس خدمة تنبيه "إنتاج
        /// اليوم" (ProductionTrendService) بس بترجّع كل العمال مش
        /// المتراجعين بس، عشان جدول "متوسط إنتاج العمال" هنا.
        /// </summary>
        private async Task LoadWorkerAveragesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var trendService = scope.ServiceProvider.GetRequiredService<ProductionTrendService>();

            _allWorkerAverages = await trendService.GetAllWorkerAveragesAsync(DateTime.Today);
            HasWorkerAverages = _allWorkerAverages.Count > 0;
            ApplyWorkerAveragesSort();
        }
    }

    public record GrainOption(ChartGrain Grain, string Display);

    public record RangeOption(int Count, string Display);
}
