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

        /// <summary>نفس عمال WorkersWithNoAttendance بالـId — لزرار "سجّل الباقيين حاضرين" في ملخص التوقيع</summary>
        public IReadOnlyList<int> WorkerIdsWithNoAttendance { get; init; } = Array.Empty<int>();

        public int ActiveProductCount { get; init; }
        public int ActiveWorkerCount { get; init; }

        /// <summary>
        /// نواقص تستاهل تأكيد صريح قبل التوقيع: حضور ناقص (بيأثر على الأجور) أو خطة ذاكرة
        /// مستحقة من غير إنتاج. منتج من غير إنتاج النهارده عادي جدًا (مش كل منتج بيتشغل
        /// كل يوم)، فإفادة بس من غير ما يوقف الزرار.
        /// </summary>
        public bool NeedsAcknowledgement => WorkersWithNoAttendance.Count > 0 || MemoryDueWithNoEntries.Count > 0;

        public bool HasAnyIssues =>
            ProductsWithNoEntries.Count > 0 || WorkersWithNoAttendance.Count > 0 || MemoryDueWithNoEntries.Count > 0;
    }
}
