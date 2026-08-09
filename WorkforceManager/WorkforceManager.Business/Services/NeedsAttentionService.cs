using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.Business.Services
{
    /// <summary>نوع الحاجة اللي محتاجة تصرّف — بيحدد اللون والأيقونة والزرار</summary>
    public enum AttentionKind
    {
        /// <summary>مرحلة مفيش عليها ولا عامل مؤهل — الإنتاج عليها مستحيل</summary>
        StageWithNoWorkers = 1,

        /// <summary>عامل غايب من غير إذن الأسبوع ده</summary>
        UnexcusedAbsence = 2,

        /// <summary>إنتاج العامل نزل عن معدّله هو</summary>
        WorkerDeclining = 3,

        /// <summary>عامل بالإنتاج ومالوش ولا مهارة — عمره ما هيظهر في رحلة إنتاج</summary>
        WorkerWithNoSkills = 4,

        /// <summary>سعر يومية مش متحدد — أجره هيطلع صفر مهما أنتج</summary>
        WorkerWithNoWage = 5,

        /// <summary>تقييمات عدى عليها شهور من غير مراجعة</summary>
        StaleRatings = 6
    }

    /// <summary>بند واحد في قايمة "محتاج تصرّف"</summary>
    public class AttentionItem
    {
        public AttentionKind Kind { get; init; }
        public required string Title { get; init; }
        public required string Detail { get; init; }

        /// <summary>العامل المقصود (لو البند بيخصّ عامل) — عشان الشاشة تفتح بروفايله</summary>
        public int? WorkerId { get; init; }

        /// <summary>ترتيب الأهمية: الأصغر أهم</summary>
        public int Severity { get; init; }
    }

    /// <summary>
    /// بيجمّع الحاجات اللي محتاجة تصرّف من المدير في مكان واحد.
    ///
    /// شاشة التقييم كانت جدول بكل العمال ومتوسطاتهم. الجدول ده بيقول
    /// حقيقة، بس مبيقولش **اعمل إيه** — والمدير بيبص عليه ويقفله.
    ///
    /// الخدمة دي بتجاوب على السؤال التاني: إيه اللي واقف، ومين اللي
    /// نزل، وإيه اللي هيوقف الإنتاج بكرة. كل بند فيه سببه ومعاه العامل
    /// المقصود عشان الشاشة تعمل منه إجراء على طول.
    ///
    /// **مفيش قاعدة جديدة هنا كمان**: التقييم بييجي من
    /// <see cref="PerformanceEvaluationService"/>، والأسبوع من
    /// <see cref="WeeklySummaryService"/>، والمهارات من
    /// <see cref="SkillRatingService"/>.
    /// </summary>
    public class NeedsAttentionService
    {
        /// <summary>تقييم عدى عليه المدة دي من غير مراجعة بيبقى محتاج نظرة</summary>
        public const int StaleRatingDays = 90;

        private readonly IWorkerRepository _workers;
        private readonly IProductRepository _products;
        private readonly IAttendanceRepository _attendance;
        private readonly IDailyProductionRepository _production;

        public NeedsAttentionService(
            IWorkerRepository workers,
            IProductRepository products,
            IAttendanceRepository attendance,
            IDailyProductionRepository production)
        {
            _workers = workers;
            _products = products;
            _attendance = attendance;
            _production = production;
        }

        /// <summary>كل اللي محتاج تصرّف في الأسبوع اللي التاريخ ده جواه</summary>
        public async Task<IReadOnlyList<AttentionItem>> GetAsync(DateTime anchor)
        {
            var (weekStart, weekEnd) = WeeklySummaryService.GetWorkWeekRange(anchor);

            var items = new List<AttentionItem>();

            items.AddRange(await StagesWithNoWorkersAsync());
            items.AddRange(await AbsencesAsync(weekStart, weekEnd));
            items.AddRange(await DecliningWorkersAsync(weekStart, weekEnd));
            items.AddRange(await WorkerSetupProblemsAsync());
            items.AddRange(await StaleRatingsAsync());

            return items
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.Title)
                .ToList();
        }

        /// <summary>
        /// مرحلة نشطة مفيش عليها ولا عامل مؤهل.
        ///
        /// أخطر بند في القايمة: شاشة التسجيل بتعرض المؤهلين بس، فالمرحلة
        /// دي **مستحيل** تتسجّل — والمستخدم بيكتشفها وهو واقف بيسجّل.
        /// </summary>
        private async Task<List<AttentionItem>> StagesWithNoWorkersAsync()
        {
            var products = await _products.GetActiveWithStagesAsync();
            var items = new List<AttentionItem>();

            foreach (var product in products)
                foreach (var stage in ProductionLine.Active(product))
                {
                    var qualified = await _workers.GetSkillsForProductAsync(product.Id);
                    if (qualified.Any(s => s.ProductionStageId == stage.Id)) continue;

                    items.Add(new AttentionItem
                    {
                        Kind = AttentionKind.StageWithNoWorkers,
                        Severity = 0,
                        Title = $"{product.Name} — {stage.StageName}",
                        Detail = "مفيش ولا عامل مؤهل للمرحلة دي، فمستحيل تتسجّل في الإنتاج اليومي"
                    });
                }

            return items;
        }

        private async Task<List<AttentionItem>> AbsencesAsync(DateTime from, DateTime to)
        {
            var records = await _attendance.GetByRangeAsync(from, to);
            var names = (await _workers.GetAllWithSkillsAsync()).ToDictionary(w => w.Id, w => w.FullName);

            return records
                .Where(a => a.Status == AttendanceStatus.AbsentWithoutPermission)
                .GroupBy(a => a.WorkerId)
                .Select(g => new AttentionItem
                {
                    Kind = AttentionKind.UnexcusedAbsence,
                    Severity = 1,
                    WorkerId = g.Key,
                    Title = names.GetValueOrDefault(g.Key, "—"),
                    Detail = g.Count() == 1
                        ? "غايب يوم من غير إذن الأسبوع ده"
                        : $"غايب {g.Count()} أيام من غير إذن الأسبوع ده"
                })
                .ToList();
        }

        /// <summary>
        /// العامل اللي إنتاجه نزل **عن معدّله هو** مش عن الفريق.
        ///
        /// ده الفرق اللي المقارنة بالفريق بتخفيه: عامل بطيء بطبيعته
        /// وثابت مش مشكلة، وعامل كان بيعمل كويس وبدأ ينزل مشكلة — حتى
        /// لو لسه فوق متوسط الفريق.
        /// </summary>
        private async Task<List<AttentionItem>> DecliningWorkersAsync(DateTime from, DateTime to)
        {
            // معدّله هو: الأربع أسابيع اللي قبل الأسبوع الحالي
            var historyFrom = from.AddDays(-28);
            var history = await _production.GetByRangeAsync(historyFrom, from.AddDays(-1));
            var current = await _production.GetByRangeAsync(from, to);

            var names = (await _workers.GetAllWithSkillsAsync()).ToDictionary(w => w.Id, w => w.FullName);

            var weekly = history
                .GroupBy(r => r.WorkerId)
                .ToDictionary(
                    g => g.Key,
                    // متوسط اليوميات في اليوم الواحد اللي اشتغله — مش
                    // مجموع الفترة، عشان عامل اشتغل يومين ميتقارنش بواحد
                    // اشتغل عشرين
                    g => g.GroupBy(r => r.Date.Date).Average(d => d.Sum(r => r.WorkdaysCompleted)));

            var items = new List<AttentionItem>();

            foreach (var g in current.GroupBy(r => r.WorkerId))
            {
                if (!weekly.TryGetValue(g.Key, out var usual) || usual <= 0) continue;

                var now = g.GroupBy(r => r.Date.Date).Average(d => d.Sum(r => r.WorkdaysCompleted));
                var ratio = now / usual;

                // 25% نزول: أقل من كده تذبذب عادي مش اتجاه
                if (ratio >= 0.75m) continue;

                items.Add(new AttentionItem
                {
                    Kind = AttentionKind.WorkerDeclining,
                    Severity = 2,
                    WorkerId = g.Key,
                    Title = names.GetValueOrDefault(g.Key, "—"),
                    Detail = $"إنتاجه نزل {(1 - ratio) * 100:0}% عن معدّله " +
                             $"({now:0.##} مقابل {usual:0.##} يومية في اليوم)"
                });
            }

            return items;
        }

        /// <summary>عامل مش هيقدر يشتغل أو يتحاسب صح بسبب بيانات ناقصة</summary>
        private async Task<List<AttentionItem>> WorkerSetupProblemsAsync()
        {
            var workers = await _workers.GetAllWithSkillsAsync();
            var items = new List<AttentionItem>();

            foreach (var worker in workers.Where(w => w.IsActive && !w.IsDeleted))
            {
                if (worker.HourlyRole is null && worker.Skills.Count == 0)
                    items.Add(new AttentionItem
                    {
                        Kind = AttentionKind.WorkerWithNoSkills,
                        Severity = 3,
                        WorkerId = worker.Id,
                        Title = worker.FullName,
                        Detail = "مالوش ولا مهارة — عمره ما هيظهر في أي رحلة إنتاج"
                    });

                if (worker.DailyWageEgp <= 0)
                    items.Add(new AttentionItem
                    {
                        Kind = AttentionKind.WorkerWithNoWage,
                        Severity = 3,
                        WorkerId = worker.Id,
                        Title = worker.FullName,
                        Detail = "مفيش سعر يومية — أجره هيطلع صفر مهما أنتج"
                    });
            }

            return items;
        }

        private async Task<List<AttentionItem>> StaleRatingsAsync()
        {
            var workers = await _workers.GetAllWithSkillsAsync();
            var cutoff = DateTime.Today.AddDays(-StaleRatingDays);

            return workers
                .Where(w => w.IsActive && !w.IsDeleted && w.Skills.Count > 0)
                .Where(w => w.Skills.All(s => s.StarsUpdatedAt is null || s.StarsUpdatedAt < cutoff))
                .Select(w => new AttentionItem
                {
                    Kind = AttentionKind.StaleRatings,
                    Severity = 4,
                    WorkerId = w.Id,
                    Title = w.FullName,
                    Detail = w.Skills.Any(s => s.StarsUpdatedAt is not null)
                        ? $"تقييماته عدى عليها أكتر من {StaleRatingDays} يوم من غير مراجعة"
                        : "تقييماته عمرها ما اتراجعت"
                })
                .ToList();
        }
    }
}
