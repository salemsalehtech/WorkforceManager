using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// قواعد شاشة الرئيسية اللي مالهاش مكان تاني في البرنامج — نقية، مفيش
    /// استعلام قاعدة بيانات جواها (نفس نمط WorkerRecognitionRules/
    /// WorkerFilterRules)، عشان الاختبارات تغطيها من غير قاعدة بيانات.
    ///
    /// **مفيش رقم بيتحسب هنا من الصفر**: كل دالة بتختار/تفلتر من نتايج
    /// خدمات موجودة أصلاً (ترتيب WorkerRecognitionRules.Rank، نشاط
    /// ProductActivityService، خطط ProductionMemoryService، سجلات الحضور).
    /// </summary>
    public static class HomeDashboardRules
    {
        /// <summary>
        /// أقل عدد عمال في الترتيب عشان "الأقل أداءً" يظهر أصلاً — 4 عشان
        /// آخر واحد مايبقاش أبدًا واحد من "أحسن 3" في نفس الوقت (قرار مؤكد
        /// مع المستخدم). أقل من كده الكارت بيعرض حالته الفاضية.
        /// </summary>
        public const int MinRankedForWorstWorker = 4;

        /// <summary>أقصى عدد أيام بيرجع لها عدّاد سلسلة الالتزام — استعلام محدود بمدى تاريخ زي باقي البرنامج</summary>
        public const int StreakLookbackDays = 60;

        /// <summary>خطة الذاكرة بتظهر في كارت الرئيسية لو تذكيرها فات أو النهارده أو خلال الأيام دي</summary>
        public const int MemoryDueSoonDays = 3;

        /// <summary>
        /// "الأقل أداءً الأسبوع ده" — آخر عنصر في نفس الترتيب اللي بيطلّع أحسن
        /// عامل (WorkerRecognitionRules.Rank)، بالظبط زي نية "أسوأ عامل" في
        /// البحث السريع (SearchIntentService). الترتيب نفسه بيستبعد عمال
        /// الساعة واللي ماأنتجش واللي صافيه صفر أو سالب، فمحدش غايب أو
        /// متجازي لحد الصفر بيتسمّى هنا — بس أضعف منتج فعلي.
        /// </summary>
        public static WorkerWeeklySummaryDto? PickWorstWorker(IReadOnlyList<WorkerWeeklySummaryDto> ranked) =>
            ranked.Count >= MinRankedForWorstWorker ? ranked[^1] : null;

        /// <summary>
        /// أكتر منتج إنتاجًا — نفس قاعدة شاشة المنتجات بالحرف
        /// (ProductsViewModel.MostActiveProduct): المنتجات اللي اشتغلت فعلاً
        /// بس، بالقطع المكتملة تنازلي، والتعادل بالاسم.
        /// </summary>
        public static ProductActivityDto? PickTopProduct(IEnumerable<ProductActivityDto> products) =>
            products.Where(p => p.WorkedInPeriod)
                .OrderByDescending(p => p.CompletedPieces).ThenBy(p => p.ProductName)
                .FirstOrDefault();

        /// <summary>
        /// أقل منتج إنتاجًا — نفس قاعدة ProductsViewModel.LeastActiveProduct،
        /// مع فرق واحد مقصود: لو منتج واحد بس اشتغل، مفيش "أقل" (هيبقى هو نفسه
        /// الأكتر، وكارتين بنفس الاسم مش معلومة).
        /// </summary>
        public static ProductActivityDto? PickBottomProduct(IEnumerable<ProductActivityDto> products)
        {
            var worked = products.Where(p => p.WorkedInPeriod).ToList();
            if (worked.Count < 2) return null;

            return worked.OrderBy(p => p.CompletedPieces).ThenBy(p => p.ProductName).First();
        }

        /// <summary>
        /// خطط الذاكرة النشطة اللي تذكيرها فات أو النهارده أو خلال
        /// <see cref="MemoryDueSoonDays"/> — الأقرب الأول.
        /// </summary>
        public static List<ProductionMemoryDto> DueSoon(
            IEnumerable<ProductionMemoryDto> memories, DateTime today, int windowDays = MemoryDueSoonDays)
        {
            var limit = today.Date.AddDays(windowDays);
            return memories
                .Where(m => m.CompletedAt is null && m.RemindOn.Date <= limit)
                .OrderBy(m => m.RemindOn).ThenBy(m => m.ProductName)
                .ToList();
        }

        /// <summary>نتيجة سلسلة الالتزام</summary>
        /// <param name="Days">عدد أيام الشغل المتتالية من غير غياب بدون إذن</param>
        /// <param name="IsCapped">السلسلة وصلت لآخر مدى البحث من غير ما تنكسر، وفيه سجلات أقدم — يعني الرقم الحقيقي أكبر ("60+")</param>
        public record StreakResult(int Days, bool IsCapped);

        /// <summary>
        /// سلسلة الالتزام على مستوى المصنع: عدد أيام الشغل المتتالية (رجوعًا
        /// من النهارده) اللي مفيش فيها ولا غياب بدون إذن.
        /// - الجمعة بتتخطى (أسبوع الشغل 6 أيام) — لا بتكسر ولا بتتعد.
        /// - يوم مالوش أي سجل حضور خالص (أجازة/المصنع قافل/النهارده لسه
        ///   ماتسجلش) بيتخطى بنفس الطريقة — غياب البيانات مش غياب عمال.
        /// - أول يوم فيه غياب بدون إذن بيقفل العد.
        ///
        /// <paramref name="records"/> ممكن تحتوي أيام أقدم من مدى البحث —
        /// وجودها هو اللي بيقول إن السلسلة "مقصوصة" (IsCapped) مش إن
        /// البرنامج لسه جديد.
        /// </summary>
        public static StreakResult ComputeStreak(
            IEnumerable<(DateTime Date, AttendanceStatus Status)> records,
            DateTime today,
            int lookbackDays = StreakLookbackDays)
        {
            var list = records.Select(r => (Date: r.Date.Date, r.Status)).ToList();
            var recordedDays = list.Select(r => r.Date).ToHashSet();
            var absenceDays = list
                .Where(r => r.Status == AttendanceStatus.AbsentWithoutPermission)
                .Select(r => r.Date)
                .ToHashSet();

            var windowStart = today.Date.AddDays(-(lookbackDays - 1));
            var streak = 0;

            for (var day = today.Date; day >= windowStart; day = day.AddDays(-1))
            {
                if (day.DayOfWeek == DayOfWeek.Friday) continue;
                if (!recordedDays.Contains(day)) continue;
                if (absenceDays.Contains(day)) return new StreakResult(streak, IsCapped: false);
                streak++;
            }

            // وصلنا لآخر المدى من غير كسر — لو فيه سجلات أقدم، الرقم الحقيقي أكبر
            return new StreakResult(streak, IsCapped: list.Any(r => r.Date < windowStart));
        }
    }
}
