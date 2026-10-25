namespace WorkforceManager.Business.DTOs
{
    /// <summary>الأرقام الحية على كروت صفحة "إدارة العمال" — شوف HubStatsService.GetWorkersStatsAsync</summary>
    public record WorkersHubStatsDto(int ActiveWorkers, int PresentToday, int AbsentToday, int PenaltiesToday, int AdjustmentsToday);

    /// <summary>الأرقام الحية على كروت صفحة "تسجيل الإنتاج اليومي" — شوف HubStatsService.GetDailyEntryStatsAsync</summary>
    public record DailyEntryHubStatsDto(int ProductionRecordsToday, int ActiveInitialBalances, int DueMemoryPlans, int ScrapPiecesToday);


    /// <summary>الأرقام الحية على كروت صفحة "إدارة الإنتاج" — شوف HubStatsService.GetProductionStatsAsync</summary>
    public record ProductionHubStatsDto(int ActiveProducts, decimal? MonthlyPlanAchievedPercent, int ProductionRecordsToday);
}
