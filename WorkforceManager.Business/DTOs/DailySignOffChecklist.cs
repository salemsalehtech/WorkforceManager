namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// اكتمال بيانات اليوم قبل التوقيع — محور مختلف تمامًا عن تغطية سجل
    /// العمليات اللي DailyOperationsSignOffService بتتحقق منها (هل كل
    /// عملية اتسجّلت اتأكّد عليها)، دي بتسأل: هل كل منتج/عامل نشط له
    /// حركة النهارده أصلًا. إفادة بس، مش بوابة حجب — شوف
    /// DailySignOffChecklistService.
    /// </summary>
    public class DailySignOffChecklist
    {
        public IReadOnlyList<string> ProductsWithNoEntries { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> WorkersWithNoAttendance { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> MemoryDueWithNoEntries { get; init; } = Array.Empty<string>();

        public bool HasAnyIssues =>
            ProductsWithNoEntries.Count > 0 || WorkersWithNoAttendance.Count > 0 || MemoryDueWithNoEntries.Count > 0;
    }
}
