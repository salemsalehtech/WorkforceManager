namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// منتج واحد في شاشة الخطة الشهرية، لشهر معيّن — المخطط له (0 لو
    /// لسه مفيش قيمة متسجلة) + بيانات كفايته (وزن/مادة) للعلامة التحذيرية.
    /// التجميع بالعيلة مسؤولية الشاشة (نفس نمط شبكة المنتجات)، مش هنا.
    /// </summary>
    public record MonthlyPlanProductDto(
        int ProductId,
        string ProductName,
        int? FamilyId,
        string? FamilyName,
        int PlannedQuantity,
        bool IsComplete);
}
