using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// صف منتج في شاشة الإحصائيات — تفاصيل الوزن ومقارنة الشهر اللي فات
    /// اللي اتنقلت من شاشة الخطة الشهرية نفسها، شوف CLAUDE.md.
    /// </summary>
    public class StatisticsProductRow
    {
        public string ProductName { get; init; } = "";
        public string? FamilyName { get; init; }
        public int EffectiveAchieved { get; init; }
        public decimal? TotalWeightGrams { get; init; }
        public decimal? TotalWeightKg => TotalWeightGrams / 1000m;
        public bool HasWeight => TotalWeightGrams is not null;

        public int? SameDayPreviousMonth { get; init; }

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

        public static StatisticsProductRow FromDto(MonthlyPlanTrackingDto p) => new()
        {
            ProductName = p.ProductName,
            FamilyName = p.FamilyName,
            EffectiveAchieved = p.EffectiveAchieved,
            TotalWeightGrams = p.PieceWeightGrams is { } w ? w * p.EffectiveAchieved : null,
            SameDayPreviousMonth = p.SameDayPreviousMonth
        };
    }

    /// <summary>مجموع وزن عيلة — للجدول الفرعي في شاشة الإحصائيات</summary>
    public class StatisticsFamilyWeightRow
    {
        public string FamilyName { get; init; } = "";
        public decimal TotalWeightGrams { get; init; }
        public decimal TotalWeightKg => TotalWeightGrams / 1000m;
    }
}
