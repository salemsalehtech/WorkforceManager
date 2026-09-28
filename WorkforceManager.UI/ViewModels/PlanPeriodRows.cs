using CommunityToolkit.Mvvm.ComponentModel;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// عنصر واحد في قايمة اختيار الفترة — Label جاهز للعرض المباشر
    /// (DisplayMemberPath)، بديل عن ربط مباشر بـPlanPeriod نفسها.
    /// </summary>
    public sealed class PlanPeriodOption
    {
        public required PlanPeriod Period { get; init; }

        public int Id => Period.Id;

        public string Label =>
            $"{Period.StartDate:yyyy/MM/dd} → {Period.EndDate:yyyy/MM/dd}  ({Period.WorkdayCount} يوم شغل)";
    }

    /// <summary>
    /// منتج واحد في شاشة الخطة بفترة مخصصة — تخطيط بس (مفيش تتبّع/محقق هنا،
    /// شوف CLAUDE.md ليه). QuantityText نص قابل للتعديل (مش int مباشرة)
    /// عشان الـTextBox يمسك حالة وسطية أثناء الكتابة، نفس نمط MonthlyPlanProductRow.
    /// </summary>
    public partial class PlanPeriodProductRow : ObservableObject
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public bool IsComplete { get; init; }

        [ObservableProperty] private string _quantityText = "0";

        public int Quantity => int.TryParse(QuantityText, out var q) ? q : 0;

        public static PlanPeriodProductRow FromDto(PlanPeriodProductDto dto) => new()
        {
            ProductId = dto.ProductId, ProductName = dto.ProductName, IsComplete = dto.IsComplete,
            QuantityText = dto.PlannedQuantity.ToString(),
        };
    }

    /// <summary>
    /// عيلة (أو "بدون عيلة") في شاشة الخطة بفترة مخصصة — الخطة هنا دايمًا
    /// SUM محسوب من منتجاتها، مفيش قيمة بتتكتب على مستوى العيلة خالص —
    /// نفس قاعدة MonthlyPlanFamilyGroupRow بالظبط.
    /// </summary>
    public partial class PlanPeriodFamilyGroupRow : ObservableObject
    {
        public string HeaderText { get; init; } = "";
        public List<PlanPeriodProductRow> Products { get; init; } = new();

        public int Subtotal => Products.Sum(p => p.Quantity);
    }
}
