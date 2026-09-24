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
    }
}
