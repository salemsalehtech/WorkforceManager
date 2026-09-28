namespace WorkforceManager.Business.DTOs
{
    /// <summary>صف واحد من لقطة محفوظة سابقًا — عرض بس، شوف MonthlyPlanSnapshot</summary>
    public record MonthlyPlanSnapshotRowDto(
        int ProductId,
        string ProductName,
        int PlannedQuantity,
        int EffectiveAchieved);
}
