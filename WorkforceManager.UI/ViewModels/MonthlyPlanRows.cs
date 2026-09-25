using CommunityToolkit.Mvvm.ComponentModel;
using WorkforceManager.Business.DTOs;

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

        partial void OnDailyTargetTextChanged(string value) => DailyTargetError = "";

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
        public bool HasWeight => TotalWeightGrams is not null;

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

        public string PercentText => AchievedPercent is { } p ? $"{p:P0}" : "—";

        public string ComparisonText => SameDayPreviousMonth is { } prev && prev > 0
            ? EffectiveAchieved >= prev ? $"▲ {(EffectiveAchieved - prev) * 100 / prev}%" : $"▼ {(prev - EffectiveAchieved) * 100 / prev}%"
            : "";

        public bool HasComparison => !string.IsNullOrEmpty(ComparisonText);

        public bool HasRequiredDailyOutput => RequiredDailyOutput is not null;
        public bool HasForecast => ForecastEndOfMonth is not null;
    }

    /// <summary>
    /// عيلة (أو "بدون عيلة") في شاشة الخطة الشهرية — الخطة والمحقق هنا
    /// دايمًا SUM محسوب من منتجاتها، مفيش قيمة بتتكتب على مستوى العيلة خالص.
    /// </summary>
    public partial class MonthlyPlanFamilyGroupRow : ObservableObject
    {
        public string HeaderText { get; init; } = "";
        public List<MonthlyPlanProductRow> Products { get; init; } = new();

        /// <summary>مجموع خطط منتجاتها — للقراءة بس</summary>
        public int Subtotal => Products.Sum(p => p.Quantity);

        public int AchievedSubtotal => Products.Sum(p => p.EffectiveAchieved);

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
        public bool HasWeight => TotalWeightGrams is not null;
    }
}
