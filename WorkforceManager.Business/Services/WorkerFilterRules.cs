using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>نوع حساب العامل — الشريحة الأساسية في شاشة العمال</summary>
    public enum WorkerPayScope
    {
        /// <summary>كل العمال النشطين</summary>
        AllActive = 0,

        /// <summary>بالإنتاج (مالوش دور بالساعة)</summary>
        ByProduction = 1,

        /// <summary>بالساعة (رص/جودة/تدريب)</summary>
        Hourly = 2,

        /// <summary>الموقوفين</summary>
        Inactive = 3
    }

    /// <summary>
    /// بيانات عامل واحد بالشكل اللي الفلترة محتاجاه — مش أكتر.
    ///
    /// نوع منفصل عن صف الشاشة عن قصد: القاعدة دي أعمال مش واجهة، ولازم
    /// تتختبر من غير ما حد يبني ViewModel.
    /// </summary>
    public class WorkerFilterSubject
    {
        public int WorkerId { get; init; }
        public bool IsActive { get; init; }
        public bool IsHourly { get; init; }

        /// <summary>المراحل اللي العامل مؤهل ليها</summary>
        public IReadOnlySet<int> StageIds { get; init; } = new HashSet<int>();

        /// <summary>المنتجات اللي عنده مهارة في أي مرحلة منها</summary>
        public IReadOnlySet<int> ProductIds { get; init; } = new HashSet<int>();

        /// <summary>متوسط نجومه على كل مهاراته (0 = مالوش مهارات)</summary>
        public decimal AverageStars { get; init; }

        /// <summary>حالة حضوره النهارده (null = مفيش تسجيل)</summary>
        public AttendanceStatus? TodayStatus { get; init; }
    }

    /// <summary>
    /// الفلاتر المختارة. كلها اختيارية، و<c>null</c> معناها "الفلتر ده
    /// مش مفعّل" مش "دوّر على قيمة فاضية".
    /// </summary>
    public class WorkerFilterCriteria
    {
        public WorkerPayScope Scope { get; init; } = WorkerPayScope.AllActive;

        /// <summary>مؤهل للمرحلة دي</summary>
        public int? StageId { get; init; }

        /// <summary>عنده مهارة في أي مرحلة من المنتج ده</summary>
        public int? ProductId { get; init; }

        /// <summary>متوسط نجومه ≥ الرقم ده</summary>
        public int? MinStars { get; init; }

        /// <summary>حالة حضوره النهارده</summary>
        public AttendanceStatus? TodayStatus { get; init; }
    }

    /// <summary>
    /// تصفية العمال — المكان الوحيد اللي بيقرر عامل يبان ولا لأ.
    ///
    /// **الفلاتر بتتجمع بـ AND**: كل فلتر مفعّل بيضيّق النتيجة، ومبيلغيش
    /// اللي قبله. ده اللي بيخلي "عمال مرحلة الدبلة + 4 نجوم فأكتر +
    /// حاضرين النهارده" سؤال واحد بدل تلات بحثات ورا بعض.
    ///
    /// الشريحة الأساسية (Scope) لوحدها بتفضل واحدة في المرة — "بالإنتاج"
    /// و"بالساعة" و"الموقوفين" مجموعات متنافية أصلاً، وتجميعها بـ AND
    /// كان هيدّي نتيجة فاضية دايمًا.
    /// </summary>
    public static class WorkerFilterRules
    {
        public static bool Matches(WorkerFilterSubject worker, WorkerFilterCriteria criteria)
        {
            if (!MatchesScope(worker, criteria.Scope)) return false;

            if (criteria.StageId is { } stageId && !worker.StageIds.Contains(stageId))
                return false;

            if (criteria.ProductId is { } productId && !worker.ProductIds.Contains(productId))
                return false;

            // مفيش مهارات = مفيش تقييم. العامل ده مش "0 نجمة"، هو خارج
            // السؤال أصلاً، فبيتستبعد من أي فلتر نجوم
            if (criteria.MinStars is { } minStars &&
                (worker.AverageStars <= 0 || worker.AverageStars < minStars))
                return false;

            // "مفيش تسجيل حضور" حالة مختلفة عن أي حالة مسجّلة — عامل
            // محدش سجّله مبيتحسبش حاضر ولا غايب
            if (criteria.TodayStatus is { } status && worker.TodayStatus != status)
                return false;

            return true;
        }

        private static bool MatchesScope(WorkerFilterSubject worker, WorkerPayScope scope) => scope switch
        {
            WorkerPayScope.ByProduction => worker.IsActive && !worker.IsHourly,
            WorkerPayScope.Hourly => worker.IsActive && worker.IsHourly,
            WorkerPayScope.Inactive => !worker.IsActive,
            _ => worker.IsActive
        };

        /// <summary>يطبّق الفلاتر على قائمة كاملة</summary>
        public static IEnumerable<T> Apply<T>(
            IEnumerable<T> items,
            Func<T, WorkerFilterSubject> selector,
            WorkerFilterCriteria criteria) =>
            items.Where(item => Matches(selector(item), criteria));
    }
}
