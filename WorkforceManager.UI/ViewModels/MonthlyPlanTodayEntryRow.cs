using CommunityToolkit.Mvvm.ComponentModel;
using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// منتج واحد في تبويب "الإنتاج اليومي" — دايمًا عن النهارده الحقيقي.
    /// IsFilled بيعتمد على وجود صف MonthlyPlanDailyEntry (مش على قيمته)،
    /// عشان الترتيب/علامة ✓ ما تتأثرش بأي حالة إنتاج حقيقي، شوف CLAUDE.md.
    /// </summary>
    public partial class MonthlyPlanTodayEntryRow : ObservableObject
    {
        /// <summary>هامش التحذير — رقم مختلف بأكتر من 15% عن الإنتاج الحقيقي (أو رقم فاضي حقيقي مع رقم مكتوب)</summary>
        private const decimal MismatchThreshold = 0.15m;

        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public int RealProductionToday { get; init; }
        public int YesterdayQuantity { get; init; }
        public bool IsFilled { get; init; }

        [ObservableProperty] private string _quantityText = "";

        public int Quantity => int.TryParse(QuantityText, out var q) ? q : 0;

        partial void OnQuantityTextChanged(string value) => OnPropertyChanged(nameof(HasMismatchWarning));

        /// <summary>تحذير غير مانع — الحفظ بيكمل عادي حتى لو ظاهر</summary>
        public bool HasMismatchWarning
        {
            get
            {
                if (!int.TryParse(QuantityText, out var manual)) return false;
                if (RealProductionToday == 0) return manual != 0;
                return Math.Abs(manual - RealProductionToday) / (decimal)RealProductionToday > MismatchThreshold;
            }
        }

        public string RealProductionTooltip => $"الإنتاج الحقيقي المسجل النهارده: {RealProductionToday}";
        public string YesterdayTooltip => $"إنتاج أمبارح المُدخل: {YesterdayQuantity}";

        public static MonthlyPlanTodayEntryRow FromDto(MonthlyPlanTodayEntryDto dto) => new()
        {
            ProductId = dto.ProductId, ProductName = dto.ProductName,
            RealProductionToday = dto.RealProductionToday, YesterdayQuantity = dto.YesterdayQuantity,
            IsFilled = dto.IsFilled, QuantityText = dto.IsFilled ? dto.ManualQuantity.ToString() : "",
        };
    }
}
