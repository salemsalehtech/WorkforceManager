using System.Windows;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// مفاتيح ألوان الحالة لشاشة الإحصائيات — نفس اللي MonthlyPlanProductRow
    /// بيستخدمها (متأخر طوبي، ماشي صح دهبي غامق، سابق دهبي غامق على تينت
    /// دهبي)، مفيش ألوان جديدة. بترجع اسم الفرشاة مش اللون، شوف ThemeBrush.
    /// </summary>
    public static class StatisticsStatusKeys
    {
        public static string Ink(PlanPaceStatus status) => status switch
        {
            PlanPaceStatus.Behind => "DangerBrush",
            PlanPaceStatus.Ahead => "GoldDeepBrush",
            _ => "GoodBrush"
        };

        public static string Tint(PlanPaceStatus status) => status switch
        {
            PlanPaceStatus.Behind => "DangerTintBrush",
            PlanPaceStatus.Ahead => "GoldTintBrush",
            _ => "GoodTintBrush"
        };

        public static string Label(PlanPaceStatus status) => status switch
        {
            PlanPaceStatus.Behind => "متأخر",
            PlanPaceStatus.Ahead => "سابق",
            _ => "ماشي صح"
        };

        /// <summary>▲ 12.5% / ▼ 3% — نفس شكل المقارنة القديم</summary>
        public static string Change(decimal? change) => change switch
        {
            null => "—",
            >= 0 => $"▲ {change:0.#}%",
            _ => $"▼ {Math.Abs(change.Value):0.#}%"
        };

        public static string ChangeKey(decimal? change) => change switch
        {
            null => "TextMutedBrush",
            >= 0 => "GoldDeepBrush",
            _ => "DangerBrush"
        };

        /// <summary>
        /// 0.52 → "52%". مش ":P0" — نمط النسبة في الثقافة العربية بيحط علامة
        /// اتجاه (ALM) جوه النص، فبتطلع مربع/بتتقص في النصوص LTR.
        /// </summary>
        public static string Percent(decimal fraction) => $"{fraction * 100m:0}%";

        /// <summary>0.034 → "3.4%" — للنسب الصغيرة زي الهالك</summary>
        public static string Percent1(decimal fraction) => $"{fraction * 100m:0.#}%";

        /// <summary>عمودين نجمة لشريط تقدّم مرسوم (المحقق / الباقي) — مقصوص بين 0 و100%</summary>
        public static GridLength BarColumn(decimal? percent) =>
            new((double)Math.Clamp(percent ?? 0, 0, 1), GridUnitType.Star);

        public static GridLength BarRestColumn(decimal? percent) =>
            new(1 - (double)Math.Clamp(percent ?? 0, 0, 1), GridUnitType.Star);

        public static string Weight(decimal grams) => grams >= 1_000_000m
            ? $"{grams / 1_000_000m:N2} طن"
            : $"{grams / 1000m:N1} كجم";
    }

    /// <summary>صف منتج — لقوايم "محتاج اهتمام"/"الأفضل أداءً" وجدول المقارنة</summary>
    public class StatisticsProductRow
    {
        public string ProductName { get; init; } = "";
        public string FamilyName { get; init; } = "";
        public int Planned { get; init; }
        public int EffectiveAchieved { get; init; }
        public decimal? PacePercent { get; init; }
        public PlanPaceStatus Status { get; init; }
        public bool IsOutsidePlan { get; init; }
        public int Gap { get; init; }
        public int? RequiredDaily { get; init; }
        public int? Forecast { get; init; }
        public int SameDayPreviousMonth { get; init; }
        public decimal? TotalWeightGrams { get; init; }

        public int RealCompleted { get; init; }
        public int ScrapPieces { get; init; }
        public decimal? ScrapPercent => MonthlyPlanStatisticsMath.ScrapPercent(RealCompleted, ScrapPieces);
        public string ScrapPercentText => ScrapPercent is { } s ? StatisticsStatusKeys.Percent1(s) : "—";
        public string ScrapDetailText => $"{ScrapPieces:N0} هالك من {RealCompleted + ScrapPieces:N0}";

        public string StatusInkKey => StatisticsStatusKeys.Ink(Status);
        public string StatusTintKey => StatisticsStatusKeys.Tint(Status);
        public string StatusLabel => IsOutsidePlan ? "خارج الخطة" : StatisticsStatusKeys.Label(Status);

        public string PaceText => PacePercent is { } p ? StatisticsStatusKeys.Percent(p) : "—";
        public string AchievedOfPlanText => Planned > 0 ? $"{EffectiveAchieved:N0} من {Planned:N0}" : $"{EffectiveAchieved:N0}";

        public string GapText => Gap < 0 ? $"ناقص {Math.Abs(Gap):N0} قطعة" : $"زيادة {Gap:N0} قطعة";
        public string RequiredDailyText => RequiredDaily is { } r ? $"{r:N0} / يوم" : "—";

        public decimal? Change => ReportBuilderService.PercentChange(EffectiveAchieved, SameDayPreviousMonth);
        public string ChangeText => StatisticsStatusKeys.Change(Change);
        public string ChangeKey => StatisticsStatusKeys.ChangeKey(Change);
        public int Difference => EffectiveAchieved - SameDayPreviousMonth;
        public string DifferenceText => Difference switch { > 0 => $"+{Difference:N0}", 0 => "0", _ => $"{Difference:N0}" };
        public string WeightText => TotalWeightGrams is { } w ? StatisticsStatusKeys.Weight(w) : "—";

        public static StatisticsProductRow FromDto(MonthlyPlanTrackingDto p, MonthlyPlanStatisticsRealDto? real = null) => new()
        {
            ProductName = p.ProductName,
            FamilyName = p.FamilyName ?? "بدون عيلة",
            Planned = p.PlannedQuantity,
            EffectiveAchieved = p.EffectiveAchieved,
            PacePercent = p.AchievedPercent,
            Status = p.Status,
            IsOutsidePlan = p.IsOutsidePlan,
            Gap = (int)Math.Round(p.EffectiveAchieved - p.ProRatedPlan),
            RequiredDaily = p.PlannedQuantity > 0 ? p.RequiredDailyOutput : null,
            Forecast = p.ForecastEndOfMonth,
            SameDayPreviousMonth = p.SameDayPreviousMonth ?? 0,
            TotalWeightGrams = p.TotalWeightGrams,
            RealCompleted = real?.RealCompleted ?? 0,
            ScrapPieces = real?.ScrapPieces ?? 0
        };
    }

    /// <summary>صف عيلة — إنجاز + إيقاع + وزن</summary>
    public class StatisticsFamilyRow
    {
        public string FamilyName { get; init; } = "";
        public int ProductCount { get; init; }
        public int Planned { get; init; }
        public int Achieved { get; init; }
        public decimal? CompletionPercent { get; init; }
        public decimal? PacePercent { get; init; }
        public PlanPaceStatus Status { get; init; }
        public decimal? WeightGrams { get; init; }
        public decimal? ScrapPercent { get; init; }
        public string ScrapText => ScrapPercent is { } s ? StatisticsStatusKeys.Percent1(s) : "—";

        public string StatusInkKey => StatisticsStatusKeys.Ink(Status);
        public string StatusTintKey => StatisticsStatusKeys.Tint(Status);
        public string StatusLabel => PacePercent is null ? "بدون خطة" : StatisticsStatusKeys.Label(Status);
        public string ProductCountText => $"{ProductCount} منتج";
        public string AchievedOfPlanText => Planned > 0 ? $"{Achieved:N0} من {Planned:N0}" : $"{Achieved:N0} قطعة";
        public string CompletionText => CompletionPercent is { } c ? StatisticsStatusKeys.Percent(c) : "—";
        public GridLength CompletionColumn => StatisticsStatusKeys.BarColumn(CompletionPercent);
        public GridLength CompletionRestColumn => StatisticsStatusKeys.BarRestColumn(CompletionPercent);
        public string PaceText => PacePercent is { } p ? "إيقاع " + StatisticsStatusKeys.Percent(p) : "";
        public string WeightText => WeightGrams is { } w ? StatisticsStatusKeys.Weight(w) : "—";

        public static StatisticsFamilyRow FromDto(MonthlyPlanStatisticsFamilyDto f) => new()
        {
            FamilyName = f.FamilyName,
            ProductCount = f.ProductCount,
            Planned = f.Planned,
            Achieved = f.Achieved,
            CompletionPercent = f.CompletionPercent,
            PacePercent = f.PacePercent,
            Status = f.Status,
            WeightGrams = f.WeightGrams,
            ScrapPercent = f.ScrapPercent
        };
    }

    /// <summary>عمود يوم من أيام الأسبوع في "إيقاع الأسبوع"</summary>
    public class StatisticsWeekdayRow
    {
        public string DayName { get; init; } = "";
        public decimal Average { get; init; }
        public int WorkdayCount { get; init; }

        /// <summary>ارتفاع العمود بالبكسل — نسبة من أعلى متوسط</summary>
        public double BarHeight { get; init; }

        public bool IsBest { get; init; }
        public string AverageText => WorkdayCount == 0 ? "—" : $"{Average:N0}";
        public string BarKey => IsBest ? "GoldDeepBrush" : "GoldBrush";
    }

    /// <summary>اختيار في قايمة فلتر الإحصائيات (عيلة أو مادة) — "الكل" أول اختيار في كل قايمة</summary>
    public class StatisticsFilterOption
    {
        public string Label { get; init; } = "";
        public bool IsAll { get; init; }
        public string? FamilyName { get; init; }
        public Core.Enums.Material? Material { get; init; }

        public static StatisticsFilterOption AllFamilies { get; } = new() { Label = "كل العيلات", IsAll = true };
        public static StatisticsFilterOption AllMaterials { get; } = new() { Label = "كل المواد", IsAll = true };

        public static StatisticsFilterOption ForFamily(string name) => new() { Label = name, FamilyName = name };

        public static StatisticsFilterOption ForMaterial(Core.Enums.Material? material) => new()
        {
            Label = material switch
            {
                Core.Enums.Material.Copper => "نحاس",
                Core.Enums.Material.Zamak => "زاما",
                _ => "غير محدد"
            },
            Material = material
        };

        public override string ToString() => Label;
    }
}
