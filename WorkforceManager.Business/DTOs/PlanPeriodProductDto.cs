namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// منتج واحد في شاشة الخطة بفترة مخصصة، لفترة معيّنة — نفس شكل
    /// MonthlyPlanProductDto بالظبط، بس لفترة (PlanPeriod) بدل شهر تقويمي.
    /// </summary>
    public record PlanPeriodProductDto(
        int ProductId,
        string ProductName,
        int? FamilyId,
        string? FamilyName,
        int PlannedQuantity,
        bool IsComplete);
}
