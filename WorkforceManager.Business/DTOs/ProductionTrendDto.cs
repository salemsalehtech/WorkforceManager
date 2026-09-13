namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// عامل إنتاجه النهارده أقل بشكل ملحوظ من متوسط آخر أيام شغله —
    /// شوف ProductionTrendService.GetDecliningWorkersAsync.
    ///
    /// المقارنة بـ**يوميات** مش قطع خام: العامل ممكن يشتغل على منتج
    /// تاني كل يوم، وكل منتج/مرحلة ليه يومية (PiecesPerWorkday) مختلفة
    /// — فمقارنة عدد القطع الخام بين يومين على مرحلتين مختلفتين مالهاش
    /// معنى (3000 قطعة على مرحلة يوميتها 5000 تراجع واضح، ونفس الـ3000
    /// على مرحلة يوميتها 600 إنتاج فوق العادة بكتير). اليومية
    /// (PieceCount / PiecesPerWorkdayAtEntry) هي الوحدة الموحّدة اللي
    /// البرنامج كله بيقارن بيها أصلاً — نفس اللي الأجر وصافي اليوميات
    /// مبنيين عليها.
    /// </summary>
    public class ProductionDeclineDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        /// <summary>يوميات النهارده (على كل المراحل)</summary>
        public decimal TodayWorkdays { get; init; }

        /// <summary>متوسط يوميات آخر أيام الشغل الفعلية قبل النهارده</summary>
        public decimal TrailingAverageWorkdays { get; init; }

        /// <summary>نسبة يوميات النهارده من المتوسط (0.7 = 70%)</summary>
        public decimal PercentOfAverage { get; init; }

        public string PercentText => $"{PercentOfAverage * 100:0}%";
        public string TodayWorkdaysText => $"{TodayWorkdays:0.##}";
        public string TrailingAverageText => $"{TrailingAverageWorkdays:0.##} يومية";
    }

    /// <summary>
    /// يوم شغل واحد في سجل العامل — لعرض "الأيام اللي قلّ فيها عن
    /// المعتاد" لما المستخدم يفتح تفاصيله في جدول "متوسط إنتاج العمال".
    /// النسبة بتتقاس على نفس متوسط العامل الحالي (مش متوسط متحرّك لكل
    /// يوم) — أبسط، وبتجاوب نفس سؤال "اليوم ده كان تحت مستواه العادي؟".
    /// </summary>
    public class WorkerProductionDayDto
    {
        public DateTime Date { get; init; }
        public decimal Workdays { get; init; }
        public int Pieces { get; init; }
        public decimal PercentOfAverage { get; init; }

        public bool IsBelowNormal => PercentOfAverage < 0.80m;

        public string DateText => Date.ToString("dd/MM");
        public string WorkdaysText => $"{Workdays:0.##} يومية ({Pieces:N0} قطعة)";
    }

    /// <summary>
    /// متوسط إنتاج عامل اليومي (آخر 7 أيام شغل فعلية له هو بس) — لجدول
    /// كل العمال مرتبين، شوف ProductionTrendService.GetAllWorkerAveragesAsync.
    /// </summary>
    public class WorkerProductionAverageDto
    {
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;

        /// <summary>متوسط يوميات آخر 7 أيام شغل فعلية — null لو لسه مفيش تاريخ كفاية</summary>
        public decimal? TrailingAverageWorkdays { get; init; }

        /// <summary>يوميات النهارده — null لو مفيش تسجيل النهارده خالص (غياب)</summary>
        public decimal? TodayWorkdays { get; init; }

        /// <summary>قطع النهارده الخام — null لو مفيش تسجيل النهارده خالص</summary>
        public int? TodayPieces { get; init; }

        /// <summary>نسبة يوميات النهارده من متوسطه هو — null لو TrailingAverageWorkdays أو TodayWorkdays مش موجودين</summary>
        public decimal? PercentOfAverage { get; init; }

        /// <summary>
        /// آخر أيام شغله (أحدث أولًا)، كل يوم مع نسبته من متوسطه —
        /// فاضية لو HasEnoughHistory false. تظهر لما المستخدم يفتح
        /// تفاصيل العامل (Expander) عشان يشوف "الأيام اللي قلّ فيها".
        /// </summary>
        public IReadOnlyList<WorkerProductionDayDto> RecentDays { get; init; } = Array.Empty<WorkerProductionDayDto>();

        public bool HasEnoughHistory => TrailingAverageWorkdays is not null;
        public bool IsBelowToday => PercentOfAverage is not null && PercentOfAverage < 0.80m;

        public string TrailingAverageText => TrailingAverageWorkdays is null ? "—" : $"{TrailingAverageWorkdays:0.##} يومية/يوم";

        /// <summary>
        /// سطر النهارده — بيظهر دايمًا لو فيه تسجيل، مش بس لما يكون فيه
        /// تحذير (عكس التصميم القديم اللي كان بيسيب السطر فاضي في
        /// الحالة العادية).
        /// </summary>
        public string TodayText => TodayWorkdays is null
            ? "لسه من غير تسجيل النهارده"
            : IsBelowToday
                ? $"اليوم: {TodayWorkdays:0.##} يومية ({TodayPieces:N0} قطعة) — ⚠ أقل من المعتاد ({PercentOfAverage:P0})"
                : $"اليوم: {TodayWorkdays:0.##} يومية ({TodayPieces:N0} قطعة)";
    }
}
