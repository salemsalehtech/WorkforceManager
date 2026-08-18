namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// عامل إنتاجه النهارده أقل بشكل ملحوظ من متوسط آخر أيام شغله —
    /// شوف ProductionTrendService.GetDecliningWorkersAsync.
    /// </summary>
    public class ProductionDeclineDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        /// <summary>مجموع قطع النهارده على كل المراحل</summary>
        public int TodayPieces { get; init; }

        /// <summary>متوسط آخر أيام الشغل الفعلية قبل النهارده</summary>
        public decimal TrailingAverage { get; init; }

        /// <summary>نسبة إنتاج النهارده من المتوسط (0.7 = 70%)</summary>
        public decimal PercentOfAverage { get; init; }

        public string PercentText => $"{PercentOfAverage * 100:0}%";
        public string TrailingAverageText => $"{TrailingAverage:N0} قطعة";
    }
}
