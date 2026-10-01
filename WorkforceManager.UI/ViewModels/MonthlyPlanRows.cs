using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// منتج واحد في شاشة الخطة الشهرية — كل أرقام التتبّع (محقق/نسبة/توقّع)
    /// نسخة عرض مباشرة من MonthlyPlanTrackingDto، مفيش حساب هنا. QuantityText/
    /// CorrectionText نص قابل للتعديل (مش int مباشرة) عشان الـTextBox يقدر
    /// يمسك حالة وسطية أثناء الكتابة؛ التحويل الفعلي بيحصل عند الحفظ (LostFocus).
    /// </summary>
    public partial class MonthlyPlanProductRow : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public bool IsComplete { get; init; }

        [ObservableProperty] private string _quantityText = "0";

        /// <summary>القيمة الرقمية الحالية — 0 لو النص مش رقم صحيح</summary>
        public int Quantity => int.TryParse(QuantityText, out var q) ? q : 0;

        /// <summary>
        /// "الخطة اليومية" — هدف يومي يدوي (فاضي = null = مفيش هدف محدد)،
        /// منفصل عن RequiredDailyOutput المحسوب. زي عمود "انتاج اليوم" في
        /// شيت المصنع القديم.
        /// </summary>
        [ObservableProperty] private string _dailyTargetText = "";
        public int? DailyTarget => int.TryParse(DailyTargetText, out var t) ? t : null;

        /// <summary>خطأ خانة الخطة اليومية للمنتج ده بالذات — تحتها بـFieldError، بيتمسح أول ما تتعدّل</summary>
        [ObservableProperty] private string _dailyTargetError = "";

        partial void OnDailyTargetTextChanged(string value)
        {
            DailyTargetError = "";
            OnPropertyChanged(nameof(HasDailyTargetConflict));
            OnPropertyChanged(nameof(DailyTargetConflictText));
        }

        // ------- تتبّع (من MonthlyPlanTrackingDto) -------

        public int AchievedToDate { get; set; }
        public int TodayCompleted { get; set; }
        public int EffectiveAchieved { get; set; }
        public decimal ProRatedPlan { get; set; }
        public decimal? AchievedPercent { get; set; }
        public PlanPaceStatus Status { get; set; }
        public int? RequiredDailyOutput { get; set; }
        public int? ForecastEndOfMonth { get; set; }
        public int? SameDayPreviousMonth { get; set; }
        public bool IsOutsidePlan { get; set; }

        /// <summary>وزن المحقق = وزن القطعة × EffectiveAchieved — null لو المنتج ماله وزن مسجّل</summary>
        public decimal? TotalWeightGrams { get; set; }

        public decimal? TotalWeightKg => TotalWeightGrams / 1000m;
        public decimal? TotalWeightTons => TotalWeightGrams / 1_000_000m;
        public bool HasWeight => TotalWeightGrams is not null;

        /// <summary>وصل لخطة الفترة كاملة قبل ما الفترة تخلص — نسخة عرض من MonthlyPlanTrackingDto.HasReachedTarget</summary>
        public bool HasReachedTarget { get; set; }

        [ObservableProperty] private string _correctionText = "0";
        public int Correction => int.TryParse(CorrectionText, out var c) ? c : 0;

        public string StatusInkKey => Status switch
        {
            PlanPaceStatus.Behind => "DangerBrush",
            PlanPaceStatus.Ahead => "GoldDeepBrush",
            _ => "GoodBrush"
        };

        public string StatusTintKey => Status switch
        {
            PlanPaceStatus.Behind => "DangerTintBrush",
            PlanPaceStatus.Ahead => "GoldTintBrush",
            _ => "GoodTintBrush"
        };

        /// <summary>0-100 لعرض العارضة — نسبة أعلى من 150% بتتقص بصريًا (العارضة مش أرقام)</summary>
        public double ProgressBarPercent => AchievedPercent is { } p ? (double)Math.Min(p * 100m, 150m) : 0;

        public string PercentText => AchievedPercent is { } p ? StatisticsStatusKeys.Percent(p) : "—";

        /// <summary>نفس مقارن النسبة المستخدم في باقي البرنامج (ReportBuilderService.PercentChange) — مفيش حساب تاني هنا</summary>
        public string ComparisonText
        {
            get
            {
                var change = ReportBuilderService.PercentChange(EffectiveAchieved, SameDayPreviousMonth);
                if (change is null) return "";
                return change >= 0 ? $"▲ {change:0.#}%" : $"▼ {Math.Abs(change.Value):0.#}%";
            }
        }

        public bool HasComparison => !string.IsNullOrEmpty(ComparisonText);

        public bool HasRequiredDailyOutput => RequiredDailyOutput is not null;
        public bool HasForecast => ForecastEndOfMonth is not null;

        /// <summary>حد الإنذار البدري — توقّع آخر الشهر أقل من 90% من الخطة (نفس حد "متأخر" في الإيقاع)</summary>
        public const decimal ForecastWarningRatio = 0.90m;

        /// <summary>إنذار بدري: بالإيقاع الحالي المنتج هيقفل الشهر تحت 90% من خطته</summary>
        public bool IsForecastBelowPlan =>
            Quantity > 0 && ForecastEndOfMonth is { } f && f < Quantity * ForecastWarningRatio;

        public string ForecastShortfallText => !IsForecastBelowPlan ? ""
            : IsClosedPeriod
                ? $"قفل ناقص {Quantity - ForecastEndOfMonth!.Value:N0} عن الخطة"
                : $"التوقّع ناقص {Quantity - ForecastEndOfMonth!.Value:N0} عن الخطة";

        /// <summary>أيام الشغل الباقية في الفترة — صفر = الشهر خلص (أو "لحد يوم" على آخره)</summary>
        public int RemainingWorkdays { get; set; }

        /// <summary>الفترة قفلت: "التوقّع" بقى هو النتيجة النهائية، و"المطلوب يوميًا" مالوش معنى</summary>
        public bool IsClosedPeriod => RemainingWorkdays == 0;

        /// <summary>
        /// الخطة اليومية اللي المستخدم كتبها × الأيام الباقية مش هتكمّل الخطة —
        /// تحذير لطيف بس، الرقم بيتحفظ عادي.
        /// </summary>
        public bool HasDailyTargetConflict =>
            DailyTarget is { } target && Quantity > 0 && RemainingWorkdays > 0
            && EffectiveAchieved + (long)target * RemainingWorkdays < Quantity;

        public string DailyTargetConflictText => !HasDailyTargetConflict ? ""
            : $"الخطة اليومية ({DailyTarget:N0}) مش هتوصّلك للهدف — محتاج {RequiredDailyOutput:N0} / يوم";

        partial void OnQuantityTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasDailyTargetConflict));
            OnPropertyChanged(nameof(DailyTargetConflictText));
        }
    }

    /// <summary>
    /// عيلة (أو "بدون عيلة") في شاشة الخطة الشهرية — الخطة والمحقق هنا
    /// دايمًا SUM محسوب من منتجاتها، مفيش قيمة بتتكتب على مستوى العيلة خالص.
    /// الكارت مقفول دايمًا في الشبكة، والضغط عليه بيفتح صفحة العيلة
    /// (MonthlyPlanViewModel.SelectedPlanFamily) — مفيش فتح/قفل جوه الكارت.
    /// </summary>
    public class MonthlyPlanFamilyGroupRow
    {
        /// <summary>"الاسم (العدد)" — للتنبيهات النصية</summary>
        public string HeaderText { get; init; } = "";

        public string FamilyName { get; init; } = "";
        public string MaterialName { get; init; } = "";

        /// <summary>مفتاح ثابت (مادة + عيلة) — صفحة العيلة المفتوحة بتتلاقى بيه تاني بعد كل إعادة تحميل</summary>
        public string Key => $"{MaterialName}|{FamilyName}";

        public List<MonthlyPlanProductRow> Products { get; init; } = new();

        public string ProductCountText => $"{Products.Count} منتج";

        /// <summary>حالة العيلة كلها (من متوسط نسب منتجاتها) — لون الكارت وفلتر "متأخر/سابق"</summary>
        public PlanPaceStatus PaceStatus => MonthlyPlanStatisticsMath.StatusOf(AveragePercent);
        public bool HasPace => AveragePercent is not null;
        public string StatusInkKey => StatisticsStatusKeys.Ink(PaceStatus);
        public string StatusTintKey => StatisticsStatusKeys.Tint(PaceStatus);

        public int ForecastBelowPlanCount => Products.Count(p => p.IsForecastBelowPlan);
        public bool HasForecastBelowPlan => ForecastBelowPlanCount > 0;
        public string ForecastBelowPlanText => IsClosedPeriod
            ? $"{ForecastBelowPlanCount} منتج قفل تحت الخطة"
            : $"{ForecastBelowPlanCount} منتج توقّعه تحت الخطة";

        /// <summary>أيام الشغل المتبقية للفترة — نفس رقم الفترة كله، لازم لحساب RequiredDailyOutputForFamily</summary>
        public int RemainingWorkdays { get; init; }

        /// <summary>أيام الشغل الكلية — مع RemainingWorkdays بتحدد علامة "المفروض لحد النهارده" على الشريط</summary>
        public int TotalWorkdays { get; init; }

        public bool IsClosedPeriod => RemainingWorkdays == 0;

        // ── شريط إنجاز الكارت: المحقق ÷ خطة العيلة، وعلامة المفروض لحد النهارده ──
        private decimal CompletionFraction => Subtotal > 0 ? Math.Min(1m, (decimal)AchievedSubtotal / Subtotal) : 0;
        private decimal ExpectedFraction => TotalWorkdays > 0 ? (decimal)(TotalWorkdays - RemainingWorkdays) / TotalWorkdays : 0;
        public System.Windows.GridLength CompletionColumn => StatisticsStatusKeys.BarColumn(CompletionFraction);
        public System.Windows.GridLength CompletionRestColumn => StatisticsStatusKeys.BarRestColumn(CompletionFraction);
        public System.Windows.GridLength ExpectedColumn => StatisticsStatusKeys.BarColumn(ExpectedFraction);
        public System.Windows.GridLength ExpectedRestColumn => StatisticsStatusKeys.BarRestColumn(ExpectedFraction);
        public string CompletionText => Subtotal > 0 ? $"{StatisticsStatusKeys.Percent((decimal)AchievedSubtotal / Subtotal)} من الخطة" : "";

        /// <summary>العمود التالت في الكارت: "مطلوب يوميًا" والشهر شغال، "النتيجة" بعد ما يقفل</summary>
        public string ThirdStatLabel => IsClosedPeriod ? "النتيجة" : "مطلوب يوميًا";
        public string ThirdStatValue => IsClosedPeriod
            ? (Subtotal > 0 ? StatisticsStatusKeys.Percent((decimal)AchievedSubtotal / Subtotal) : "—")
            : RequiredDailyOutputForFamilyText;

        /// <summary>مجموع خطط منتجاتها — للقراءة بس</summary>
        public int Subtotal => Products.Sum(p => p.Quantity);

        public int AchievedSubtotal => Products.Sum(p => p.EffectiveAchieved);

        /// <summary>
        /// الإنتاج اليومي المطلوب على مستوى العيلة كلها — صيغة واحدة محسوبة
        /// مباشرة على إجمالي العيلة (Subtotal - AchievedSubtotal) ÷ الأيام
        /// المتبقية، مش مجموع أرقام منتجات مقرّبة كل واحدة لوحدها (ده بالظبط
        /// الفرق عن باگ الشيت القديم في تقريب/توزيع الصيغة). null لو مفيش
        /// أيام متبقية.
        /// </summary>
        public int? RequiredDailyOutputForFamily => MonthlyPlanFamilyMath.RequiredDailyOutput(Subtotal, AchievedSubtotal, RemainingWorkdays);

        public bool HasRequiredDailyOutputForFamily => RequiredDailyOutputForFamily is not null;

        /// <summary>نص جاهز لعمود "مطلوب يوميًا" في كارت العيلة — "—" لو مفيش (شوف MonthlyPlanView)</summary>
        public string RequiredDailyOutputForFamilyText =>
            HasRequiredDailyOutputForFamily ? RequiredDailyOutputForFamily!.Value.ToString() : "—";

        /// <summary>متوسط نسبة المحقق عبر منتجات العيلة اللي ليها خطة فعلية — أساس تنبيه "العيلة واطية"</summary>
        public decimal? AveragePercent
        {
            get
            {
                var withPlan = Products.Where(p => p.AchievedPercent is not null).ToList();
                return withPlan.Count == 0 ? null : withPlan.Average(p => p.AchievedPercent!.Value);
            }
        }

        /// <summary>تنبيه لطيف — العيلة كلها واطية عن الإيقاع (أقل من 75% كمتوسط)</summary>
        public bool IsBelowThreshold => AveragePercent is { } avg && avg < 0.75m;

        /// <summary>نسبة إنجاز العيلة على رأس كارتها — ذهبي/بني دايمًا (مش أحمر/أخضر حالة)، بمعزل عن AveragePercent المستخدم للتنبيه بس</summary>
        public string CardPercentText => AveragePercent is { } p ? StatisticsStatusKeys.Percent(p) : "—";

        /// <summary>مجموع تصليحات منتجاتها — SUM بسيط، نفس منطق Subtotal بالظبط</summary>
        public int CorrectionsSubtotal => Products.Sum(p => p.Correction);

        /// <summary>مجموع وزن المحقق لمنتجاتها — null لو ولا منتج فيها له وزن مسجّل</summary>
        public decimal? TotalWeightGrams
        {
            get
            {
                var withWeight = Products.Where(p => p.TotalWeightGrams is not null).ToList();
                return withWeight.Count == 0 ? null : withWeight.Sum(p => p.TotalWeightGrams!.Value);
            }
        }

        public decimal? TotalWeightKg => TotalWeightGrams / 1000m;
        public decimal? TotalWeightTons => TotalWeightGrams / 1_000_000m;
        public bool HasWeight => TotalWeightGrams is not null;

        public bool HasCorrections => CorrectionsSubtotal != 0;

        /// <summary>
        /// فرق "إجمالي الانتاج اليومي" على مستوى المجموعة — مجموع المطلوب
        /// يوميًا لكل منتجاتها ناقص مجموع اللي اتعمل فيهم النهارده فعلاً.
        /// سالب = المجموعة لسه ناقصة عن المطلوب اليومي، موجب = فوق المطلوب.
        /// null لو ولا منتج فيها له RequiredDailyOutput (كلهم خلصوا الشهر أو من غير خطة).
        /// </summary>
        public int? DailyGap
        {
            get
            {
                var withRequirement = Products.Where(p => p.RequiredDailyOutput is not null).ToList();
                return withRequirement.Count == 0
                    ? null
                    : withRequirement.Sum(p => p.TodayCompleted) - withRequirement.Sum(p => p.RequiredDailyOutput!.Value);
            }
        }

        public bool HasDailyGap => DailyGap is not null;
        public bool IsDailyGapNegative => DailyGap is { } gap && gap < 0;
    }

    /// <summary>
    /// مادة (نحاس/زاما/غير محدد) في شاشة الخطة الشهرية — أعلى مستوى تجميع،
    /// زي شيت المصنع (قسم نحاس كامل، قسم زاما كامل، كل واحد بإجمالياته).
    /// خطة/محقق/وزن المادة دايمًا SUM من عائلاتها، مفيش كتابة على المستوى ده.
    /// </summary>
    public partial class MonthlyPlanMaterialGroupRow : ObservableObject
    {
        public string HeaderText { get; init; } = "";
        public List<MonthlyPlanFamilyGroupRow> FamilyGroups { get; init; } = new();

        private IEnumerable<MonthlyPlanProductRow> AllProducts => FamilyGroups.SelectMany(g => g.Products);

        public int Subtotal => AllProducts.Sum(p => p.Quantity);
        public int AchievedSubtotal => AllProducts.Sum(p => p.EffectiveAchieved);

        public decimal? TotalWeightGrams
        {
            get
            {
                var withWeight = AllProducts.Where(p => p.TotalWeightGrams is not null).ToList();
                return withWeight.Count == 0 ? null : withWeight.Sum(p => p.TotalWeightGrams!.Value);
            }
        }

        public decimal? TotalWeightKg => TotalWeightGrams / 1000m;
        public decimal? TotalWeightTons => TotalWeightGrams / 1_000_000m;
        public bool HasWeight => TotalWeightGrams is not null;
    }
}
