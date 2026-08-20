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

    /// <summary>
    /// متوسط إنتاج عامل اليومي (آخر 7 أيام شغل فعلية له هو بس) — لجدول
    /// كل العمال مرتبين، شوف ProductionTrendService.GetAllWorkerAveragesAsync.
    /// </summary>
    public class WorkerProductionAverageDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        /// <summary>متوسط آخر 7 أيام شغل فعلية — null لو لسه مفيش تاريخ كفاية</summary>
        public decimal? TrailingAverage { get; init; }

        /// <summary>قطع النهارده — null لو مفيش تسجيل النهارده خالص (غياب)</summary>
        public int? TodayPieces { get; init; }

        /// <summary>نسبة إنتاج النهارده من متوسطه هو — null لو TrailingAverage أو TodayPieces مش موجودين</summary>
        public decimal? PercentOfAverage { get; init; }

        public bool HasEnoughHistory => TrailingAverage is not null;
        public bool IsBelowToday => PercentOfAverage is not null && PercentOfAverage < 0.80m;

        public string TrailingAverageText => TrailingAverage is null ? "—" : $"{TrailingAverage:N0} قطعة/يوم";

        /// <summary>
        /// فاضي لو مفيش تاريخ كفاية أو مفيش تسجيل النهارده خالص (غياب) —
        /// "عادي"/التحذير بس لما يكون فيه رقم فعلي النهارده يتقارن بيه.
        /// </summary>
        public string StatusText => PercentOfAverage is null ? ""
            : IsBelowToday ? $"⚠ أقل من المعتاد ({PercentOfAverage:P0})"
            : "عادي";
    }
}
