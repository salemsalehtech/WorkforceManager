using System.Windows;
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
        public int? FamilyId { get; init; }
        public string? FamilyName { get; init; }
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
            FamilyId = dto.FamilyId, FamilyName = dto.FamilyName,
            RealProductionToday = dto.RealProductionToday, YesterdayQuantity = dto.YesterdayQuantity,
            IsFilled = dto.IsFilled, QuantityText = dto.IsFilled ? dto.ManualQuantity.ToString() : "",
        };
    }

    /// <summary>
    /// عيلة (أو "بدون عيلة") جوّه تبويب "الإنتاج اليومي" — كارت مقفول في
    /// الشبكة، والضغط عليه بيفتح صفحة العيلة لوحدها للتسجيل
    /// (MonthlyPlanViewModel.SelectedTodayFamily). مفيش فتح/قفل جوه الكارت
    /// نفسه خلاص: الكروت المفتوحة كانت بتطوّل الشبكة وبتنزل تحت بعض.
    /// الصفوف بتتبني من جديد كل تحميل (مش mutation حي)، فكل الخصايص محسوبة عادي.
    /// </summary>
    public class MonthlyPlanTodayFamilyGroupRow
    {
        public string HeaderText { get; init; } = "";
        public List<MonthlyPlanTodayEntryRow> Products { get; init; } = new();

        public bool AllFilled => Products.Count > 0 && Products.All(p => p.IsFilled);
        public int FilledCount => Products.Count(p => p.IsFilled);
        public int TotalCount => Products.Count;
        public int RemainingCount => TotalCount - FilledCount;

        public string ProgressText => $"{FilledCount} من {TotalCount} اتسجلوا";

        public string StatusText => AllFilled ? "خلصت" : FilledCount == 0 ? "لسه ماتسجلش" : $"فاضل {RemainingCount}";

        /// <summary>شريط التقدّم: عمودين نجمة (المتسجل / الباقي)</summary>
        public GridLength FilledColumn => new(FilledCount, GridUnitType.Star);
        public GridLength RemainingColumn => new(RemainingCount, GridUnitType.Star);
    }
}
