using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// سهم مقارنة الأسبوع ده باللي فات على كارت — نص + مفتاحين فُرَش
    /// (حبر + خلفية فاتحة من نفس العيلة Good/Danger) عشان التبديل الحي
    /// للثيم يوصله زي أي عنصر تاني (ThemeBrush.ForegroundKey/BackgroundKey).
    /// </summary>
    public sealed record TrendBadge(string Text, string InkKey, string TintKey)
    {
        /// <param name="percent">من HomeDashboardRules.Trend — null = مفيش سهم خالص</param>
        /// <param name="higherIsBetter">false للغياب: النزول هو الخبر الحلو</param>
        public static TrendBadge? For(int? percent, bool higherIsBetter = true)
        {
            if (percent is not { } p) return null;

            // صفر تغيير: علامة محايدة بدل سهم أخضر/أحمر لحاجة ماتحركتش
            if (p == 0) return new TrendBadge("= 0%", "TextMutedBrush", "SurfaceAltBrush");

            var good = p > 0 == higherIsBetter;
            return new TrendBadge(
                $"{(p > 0 ? "▲" : "▼")} {Math.Abs(p)}%",
                good ? "GoodBrush" : "DangerBrush",
                good ? "GoodTintBrush" : "DangerTintBrush");
        }
    }

    /// <summary>عامل في كارت الرئيسية (أحسن/أقل أداءً) — جاهز للعرض</summary>
    public sealed record HomeWorkerCard(
        int WorkerId, string Name, string Initials, int Pieces, decimal NetWorkdays, TrendBadge? Trend);

    /// <summary>منتج في كارت الرئيسية (أكتر/أقل منتج) — جاهز للعرض</summary>
    public sealed record HomeProductCard(int ProductId, string Name, int Pieces, TrendBadge? Trend);

    /// <summary>خطة ذاكرة في كارت التذكير — الخطة الأصلية + نص الموعد</summary>
    public sealed record HomeMemoryItem(ProductionMemoryDto Memory, string DueText, bool IsLate)
    {
        public bool HasNotes => !string.IsNullOrWhiteSpace(Memory.Notes);
    }

    /// <summary>
    /// عقل شاشة "الرئيسية" — أول شاشة تظهر بعد تسجيل الدخول، وبترجع
    /// إليها من عنصر التنقّل أو لوجو WMS في أي وقت.
    ///
    /// الشاشة دي **متحسبش أي رقم جديد** — كل خاصية هنا نسخة عرض من
    /// HomeSummaryDto (HomeSummaryService) أو من نفس ProductionChartService
    /// اللي شاشة التقييم بترسم منه. أسهم المقارنة من HomeDashboardRules.Trend
    /// (اللي هي نفسها ReportBuilderService.PercentChange).
    /// </summary>
    public partial class HomeViewModel : ObservableObject
    {
        /// <summary>أقصى ارتفاع لعمود الرسم هنا — أقصر من التقييم (260) لأنه ملخص مش أداة تحليل</summary>
        public const double WeekChartMaxBarHeight = 150;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurrentUserContext _currentUser;

        public HomeViewModel(IServiceScopeFactory scopeFactory, CurrentUserContext currentUser)
        {
            _scopeFactory = scopeFactory;
            _currentUser = currentUser;
        }

        // جمل تحفيزية ثابتة — تشكيل عرض بحت، بتتختار حسب يوم السنة عشان
        // تفضل نفسها طول اليوم بدل ما تتغيّر كل مرة الشاشة تتحمّل
        private static readonly string[] MotivationalMessages =
        {
            "يوم جديد، فرصة جديدة تزوّد الإنتاج.",
            "الفريق شغال كويس — يلا نكمل بنفس الحماس.",
            "كل قطعة بتتسجل بتقرب المصنع لهدفه.",
            "ابدأ يومك بخطوة، والباقي هيجري لوحده.",
            "شغل اليوم هو إنجاز بكرة."
        };

        [ObservableProperty] private bool _isBusy;

        // ═══════════ الترحيب ═══════════

        [ObservableProperty] private string _welcomeText = "مرحبًا";
        [ObservableProperty] private string _todayText = string.Empty;
        [ObservableProperty] private string _weekRangeText = string.Empty;
        [ObservableProperty] private string _daysLeftInWeekText = string.Empty;
        [ObservableProperty] private string _motivationalMessage = string.Empty;

        // ═══════════ أرقام الأسبوع (KPI) ═══════════

        [ObservableProperty] private int _totalPiecesThisWeek;
        [ObservableProperty] private TrendBadge? _piecesTrend;

        [ObservableProperty] private int _activeWorkersThisWeek;
        [ObservableProperty] private TrendBadge? _activeWorkersTrend;

        [ObservableProperty] private decimal _netWorkdaysThisWeek;
        [ObservableProperty] private TrendBadge? _netWorkdaysTrend;

        [ObservableProperty] private int _unexcusedAbsencesThisWeek;
        [ObservableProperty] private TrendBadge? _absencesTrend;

        /// <summary>"نسبة الحضور 94%" تحت رقم الغياب — فاضي لو مفيش سجل حضور خالص (مش "0%")</summary>
        [ObservableProperty] private string _attendanceRateText = string.Empty;

        // ═══════════ الناس والمنتجات ═══════════

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBestWorker))]
        private HomeWorkerCard? _bestWorker;

        public bool HasBestWorker => BestWorker is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasWorstWorker))]
        private HomeWorkerCard? _worstWorker;

        public bool HasWorstWorker => WorstWorker is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasTopProduct))]
        private HomeProductCard? _topProduct;

        public bool HasTopProduct => TopProduct is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBottomProduct))]
        private HomeProductCard? _bottomProduct;

        public bool HasBottomProduct => BottomProduct is not null;

        // ═══════════ خطة الشهر ═══════════

        /// <summary>نسبة محقق الخطة الشهرية إجمالاً لحد النهارده — null لو مفيش خطة مسجّلة للشهر ده خالص</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMonthlyPlanData))]
        private decimal? _monthlyPlanAchievedPercent;

        public bool HasMonthlyPlanData => MonthlyPlanAchievedPercent is not null;

        public string MonthlyPlanPercentText => MonthlyPlanAchievedPercent is { } p ? $"{p:P0}" : "";

        // ═══════════ سلسلة الالتزام ═══════════

        [ObservableProperty] private string _streakNumberText = "0";
        [ObservableProperty] private string _streakCaption = string.Empty;
        [ObservableProperty] private bool _hasStreak;

        // ═══════════ تذكير الذاكرة + محتاج انتباهك ═══════════

        public ObservableCollection<HomeMemoryItem> DueMemories { get; } = new();

        [ObservableProperty] private bool _hasDueMemories;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStaleBalances))]
        private int _staleBalanceCount;

        public bool HasStaleBalances => StaleBalanceCount > 0;

        // ═══════════ رسم الأسبوع ═══════════

        public ObservableCollection<ChartBucket> WeekChartBuckets { get; } = new();

        [ObservableProperty] private bool _weekChartHasData;

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                var today = DateTime.Today;
                using var scope = _scopeFactory.CreateScope();
                var summary = await scope.ServiceProvider.GetRequiredService<HomeSummaryService>().GetSummaryAsync(today);

                // نفس ProductionChartService اللي التقييم بيرسم منه، على أسبوع الشغل
                // الحالي بس (GetWorkWeekRange) بالتقسيم اليومي — نفس الـscope بالتتابع
                // (DbContext مابيستحملش استعلامين متوازيين)
                var points = await scope.ServiceProvider.GetRequiredService<ProductionChartService>()
                    .GetProductOutputAsync(summary.WeekStart, summary.WeekEnd, ChartGrain.Day);

                ApplyHeader(summary, today);
                ApplyKpis(summary);
                ApplyPeopleAndProducts(summary);
                ApplyStreak(summary);
                ApplyMemories(summary, today);
                StaleBalanceCount = summary.StaleInitialBalances.Count;
                ApplyChart(points, summary);
            }
            finally { IsBusy = false; }
        }

        private void ApplyHeader(HomeSummaryDto summary, DateTime today)
        {
            // الاسم الظاهر من غير الدور الإداري (ActorName بيضيفه للسجل مش للترحيب)
            var name = _currentUser.DisplayName ?? _currentUser.Username;
            WelcomeText = string.IsNullOrWhiteSpace(name) ? "مرحبًا" : $"مرحبًا، {name}";
            TodayText = today.ToString("dddd، d MMMM yyyy");
            WeekRangeText = $"أسبوع الشغل من {summary.WeekStart:d MMMM} إلى {summary.WeekEnd:d MMMM}";
            MotivationalMessage = MotivationalMessages[today.DayOfYear % MotivationalMessages.Length];

            var daysLeft = (summary.WeekEnd.Date - today).Days;
            DaysLeftInWeekText = daysLeft switch
            {
                <= 0 => "آخر يوم في أسبوع الشغل",
                1 => "باقي يوم واحد",
                2 => "باقي يومين",
                _ => $"باقي {daysLeft} أيام"
            };
        }

        private void ApplyKpis(HomeSummaryDto s)
        {
            TotalPiecesThisWeek = s.TotalPiecesThisWeek;
            PiecesTrend = TrendBadge.For(HomeDashboardRules.Trend(s.TotalPiecesThisWeek, s.PreviousWeekTotalPieces));

            ActiveWorkersThisWeek = s.ActiveWorkersThisWeek;
            ActiveWorkersTrend = TrendBadge.For(HomeDashboardRules.Trend(s.ActiveWorkersThisWeek, s.PreviousWeekActiveWorkers));

            NetWorkdaysThisWeek = s.NetWorkdaysThisWeek;
            NetWorkdaysTrend = TrendBadge.For(HomeDashboardRules.Trend(s.NetWorkdaysThisWeek, s.PreviousWeekNetWorkdays));

            UnexcusedAbsencesThisWeek = s.UnexcusedAbsencesThisWeek;
            AbsencesTrend = TrendBadge.For(
                HomeDashboardRules.Trend(s.UnexcusedAbsencesThisWeek, s.PreviousWeekUnexcusedAbsences),
                higherIsBetter: false);

            AttendanceRateText = s.AttendanceRatePercent is { } rate ? $"نسبة الحضور {rate:0}%" : string.Empty;
        }

        private void ApplyPeopleAndProducts(HomeSummaryDto s)
        {
            BestWorker = ToWorkerCard(s.BestWorkerOfWeek, s.BestWorkerPreviousPieces);
            WorstWorker = ToWorkerCard(s.WorstWorkerOfWeek, s.WorstWorkerPreviousPieces);
            TopProduct = ToProductCard(s.TopProduct);
            BottomProduct = ToProductCard(s.BottomProduct);
            MonthlyPlanAchievedPercent = s.MonthlyPlanAchievedPercent;
        }

        private static HomeWorkerCard? ToWorkerCard(WorkerWeeklySummaryDto? w, int previousPieces) => w is null
            ? null
            : new HomeWorkerCard(w.WorkerId, w.WorkerName, NameInitials.From(w.WorkerName), w.TotalPieces,
                w.NetWorkdays, TrendBadge.For(HomeDashboardRules.Trend(w.TotalPieces, previousPieces)));

        private static HomeProductCard? ToProductCard(HomeProductStat? p) => p is null
            ? null
            : new HomeProductCard(p.ProductId, p.ProductName, p.Pieces,
                TrendBadge.For(HomeDashboardRules.Trend(p.Pieces, p.PreviousPieces)));

        private void ApplyStreak(HomeSummaryDto s)
        {
            HasStreak = s.StreakDays > 0;
            StreakNumberText = s.StreakIsCapped ? $"{s.StreakDays}+" : s.StreakDays.ToString();

            // الصفر بيشجّع مش بيلوم — السلسلة الجديدة بتبدأ من النهارده
            StreakCaption = s.StreakDays switch
            {
                0 => "النهارده بداية سلسلة جديدة — يوم من غير غياب بدون إذن ويبدأ العدّ",
                1 => "يوم شغل من غير غياب بدون إذن — كمّلوا",
                2 => "يومين شغل متتاليين من غير غياب بدون إذن",
                <= 10 => "أيام شغل متتالية من غير غياب بدون إذن",
                _ => "يوم شغل متتالي من غير غياب بدون إذن 👏"
            };
        }

        private void ApplyMemories(HomeSummaryDto s, DateTime today)
        {
            DueMemories.Clear();
            foreach (var m in s.DueSoonMemories)
            {
                var days = (m.RemindOn.Date - today).Days;
                var dueText = days switch
                {
                    < 0 => $"متأخرة من {m.RemindOn:d MMMM}",
                    0 => "النهارده",
                    1 => "بكرة",
                    _ => $"بعد {days} أيام — {m.RemindOn:dddd d MMMM}"
                };
                DueMemories.Add(new HomeMemoryItem(m, dueText, IsLate: days <= 0));
            }
            HasDueMemories = DueMemories.Count > 0;
        }

        private void ApplyChart(IReadOnlyList<ProductOutputPointDto> points, HomeSummaryDto s)
        {
            // lastDay = آخر يوم في الأسبوع: الأيام الجاية بتظهر أعمدة فاضية فالأسبوع
            // كله باين. مفيش مفتاح ألوان/مقارنة هنا، فالفترة اللي قبلها فاضية
            var chart = ProductOutputChartBuilder.Build(
                points, s.WeekStart, s.WeekEnd, ChartGrain.Day,
                new Dictionary<int, int>(), WeekChartMaxBarHeight);

            WeekChartBuckets.Clear();
            foreach (var bucket in chart.Buckets) WeekChartBuckets.Add(bucket);
            WeekChartHasData = chart.HasData;
        }
    }
}
