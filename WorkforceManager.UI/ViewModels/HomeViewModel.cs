using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Helpers;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عقل شاشة "الرئيسية" — أول شاشة تظهر بعد تسجيل الدخول، وبترجع
    /// إليها من عنصر التنقّل في أي وقت.
    ///
    /// الشاشة دي **متحسبش أي رقم جديد** — كل خاصية هنا نسخة عرض مباشرة
    /// من HomeSummaryDto (اللي هو نفسه أصلاً تجميع بدون حساب فوق
    /// WeeklySummaryService/InitialBalanceService/ProductionMemoryService).
    /// </summary>
    public partial class HomeViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public HomeViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
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

        [ObservableProperty] private string _weekRangeText = string.Empty;

        [ObservableProperty] private string _motivationalMessage = string.Empty;

        [ObservableProperty] private int _totalPiecesThisWeek;

        [ObservableProperty] private int _activeWorkersThisWeek;

        [ObservableProperty] private decimal _netWorkdaysThisWeek;

        [ObservableProperty] private string _daysLeftInWeekText = string.Empty;

        /// <summary>نسبة تغيير قطع الأسبوع عن اللي فاته — null لو الأسبوع اللي فات كان صفر (نسبة غير معرّفة رياضيًا، مش "صفر تغيير")</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPieceComparison))]
        [NotifyPropertyChangedFor(nameof(PieceChangeIsPositive))]
        [NotifyPropertyChangedFor(nameof(PieceChangeDisplayText))]
        private int? _pieceChangePercent;

        public bool HasPieceComparison => PieceChangePercent is not null;

        public bool PieceChangeIsPositive => (PieceChangePercent ?? 0) >= 0;

        /// <summary>"▲ 12%"/"▼ 8%" جاهز للعرض — قيمة مطلقة لأن السهم نفسه بيقول الاتجاه</summary>
        public string PieceChangeDisplayText =>
            PieceChangePercent is null ? string.Empty
            : $"{(PieceChangeIsPositive ? "▲" : "▼")} {Math.Abs(PieceChangePercent.Value)}%";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasTopProduct))]
        private string? _topProductName;

        [ObservableProperty] private int _topProductPieces;

        public bool HasTopProduct => TopProductName is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAttendanceRate))]
        private decimal? _attendanceRatePercent;

        public bool HasAttendanceRate => AttendanceRatePercent is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBestWorker))]
        private string? _bestWorkerName;

        [ObservableProperty] private string _bestWorkerInitials = string.Empty;

        public bool HasBestWorker => BestWorkerName is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStaleBalances))]
        [NotifyPropertyChangedFor(nameof(HasAnyAttentionItems))]
        private int _staleBalanceCount;

        public bool HasStaleBalances => StaleBalanceCount > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDueMemories))]
        [NotifyPropertyChangedFor(nameof(HasAnyAttentionItems))]
        private int _dueMemoriesCount;

        public bool HasDueMemories => DueMemoriesCount > 0;

        /// <summary>لو الاتنين صفر، الكارت بيعرض سطر إيجابي بدل ما يتخبّى خالص — تأكيد إيجابي مش بس غياب تنبيه</summary>
        public bool HasAnyAttentionItems => HasStaleBalances || HasDueMemories;

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<HomeSummaryService>();
                var summary = await service.GetSummaryAsync(DateTime.Today);

                WeekRangeText = $"الأسبوع من {summary.WeekStart:d MMMM} إلى {summary.WeekEnd:d MMMM}";
                MotivationalMessage = MotivationalMessages[DateTime.Today.DayOfYear % MotivationalMessages.Length];

                TotalPiecesThisWeek = summary.TotalPiecesThisWeek;
                ActiveWorkersThisWeek = summary.ActiveWorkersThisWeek;
                NetWorkdaysThisWeek = summary.NetWorkdaysThisWeek;

                var daysLeft = (summary.WeekEnd.Date - DateTime.Today.Date).Days;
                DaysLeftInWeekText = daysLeft switch
                {
                    <= 0 => "آخر يوم في أسبوع الشغل",
                    1 => "باقي يوم واحد من أسبوع الشغل",
                    2 => "باقي يومين من أسبوع الشغل",
                    _ => $"باقي {daysLeft} أيام من أسبوع الشغل"
                };

                // النسبة مش معرّفة رياضيًا لو الأسبوع اللي فات كان صفر —
                // بنسيب المقارنة مختفية بدل ما نعرض "+∞%" أو نلفّق صفر
                PieceChangePercent = summary.PreviousWeekTotalPieces > 0
                    ? (int)Math.Round(
                        (summary.TotalPiecesThisWeek - summary.PreviousWeekTotalPieces)
                        * 100.0 / summary.PreviousWeekTotalPieces)
                    : null;

                TopProductName = summary.TopProduct?.ProductName;
                TopProductPieces = summary.TopProduct?.Pieces ?? 0;

                AttendanceRatePercent = summary.AttendanceRatePercent;

                BestWorkerName = summary.BestWorkerOfWeek?.WorkerName;
                BestWorkerInitials = summary.BestWorkerOfWeek is null
                    ? string.Empty
                    : NameInitials.From(summary.BestWorkerOfWeek.WorkerName);

                StaleBalanceCount = summary.StaleInitialBalances.Count;
                // مؤقت لحد إعادة تصميم الشاشة: نفس معنى "مستحقة" القديم (فات أو النهارده)
                DueMemoriesCount = summary.DueSoonMemories.Count(m => m.RemindOn.Date <= DateTime.Today);
            }
            finally { IsBusy = false; }
        }
    }
}
