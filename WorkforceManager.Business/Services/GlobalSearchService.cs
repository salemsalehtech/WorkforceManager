using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// منسّق "بحث سريع" الشامل: بيحمّل الفئات الثمانية المرتبطة بقاعدة
    /// البيانات (العمال، المنتجات، مراحل الإنتاج، الرصيد الأولي، خطط
    /// الذاكرة، سجل العمليات، قوالب التقارير، الحسابات الإدارية)،
    /// ويشغّل <see cref="SearchMatcher"/> عليها، ويرجّع نتيجة موحّدة
    /// مرتبة. الفئتين المتبقيتين (الإعدادات والدليل) محتوى ثابت في
    /// الواجهة — الواجهة هي اللي بتضيفهم لنفس القايمة بعد ما الخدمة دي
    /// ترجّع نتايجها، شوف <see cref="SearchCategory"/>.
    ///
    /// كل حمولة فئة بتتعمل بالتوازي (<see cref="Task.WhenAll"/>) — سبع
    /// استعلامات مستقلة (الحسابات الإدارية والعمال بيشاركوا نفس الـ
    /// repository بس باستدعاءين منفصلين)، مفيش واحد فيهم محتاج نتيجة التاني.
    /// </summary>
    public class GlobalSearchService
    {
        private readonly IWorkerRepository _workers;
        private readonly IProductRepository _products;
        private readonly InitialBalanceService _initialBalances;
        private readonly ProductionMemoryService _memory;
        private readonly ActivityLogService _activityLog;

        /// <summary>
        /// null = المسار الافتراضي الحقيقي (AppPaths.DataFolder). الاختبارات
        /// بتمرّر مسار مؤقت معزول — نفس القاعدة اللي ReportTemplateStore
        /// نفسها موثّقة بيها في كل اختباراتها الحالية (ReportTemplateTests.cs)،
        /// عشان اختبار البحث الشامل ميتأثرش بقوالب حقيقية محفوظة على جهاز
        /// المطوّر، ولا يكتب/يقرا من مجلد الإنتاج الحقيقي أثناء الاختبار.
        /// </summary>
        private readonly string? _reportTemplatesPath;

        /// <summary>
        /// أعلى عدد نتايج لكل فئة — سجل العمليات هو الفئة الأكبر حجمًا
        /// بمراحل، فمن غيره كان بيغرق باقي الفئات التسعة في قايمة نتايج
        /// طويلة. المستخدم غالبًا مهتم بأقرب كام تطابق، مش بكل تطابق ممكن.
        /// </summary>
        public const int MaxResultsPerCategory = 8;

        /// <summary>
        /// مدى تاريخ تحميل سجل العمليات للبحث — **مش الجدول كله**، عشان ده
        /// الجدول الوحيد اللي ممكن يكبر بجد على مدى سنين. لكن سياسة
        /// الاحتفاظ الموجودة فعلًا (AppSettings.ActivityLogRetentionDays/
        /// ActivityLogFinancialRetentionDays) بتحدّ الجدول الحي أصلًا —
        /// أطول سياسة احتفاظ 365 يوم (أحداث الفلوس)، فـ400 يوم هامش أمان
        /// فوقها يغطي عمليًا كل صف حي في الجدول، من غير تحميل غير محدود.
        /// </summary>
        public const int ActivityLogSearchWindowDays = 400;

        /// <summary>وزن الحقل الأساسي (الاسم) عند تجميع أعلى نتيجة — public عشان الواجهة تستخدم نفس الوزن بالظبط لفئتي الإعدادات/الدليل (محتوى ثابت مش من هنا)</summary>
        public const double PrimaryFieldWeight = 1.0;

        /// <summary>وزن الحقل الثانوي (ملاحظات/سياق) — أقل من الأساسي عن قصد، شوف BestMatch</summary>
        public const double SecondaryFieldWeight = 0.6;

        public GlobalSearchService(
            IWorkerRepository workers,
            IProductRepository products,
            InitialBalanceService initialBalances,
            ProductionMemoryService memory,
            ActivityLogService activityLog,
            string? reportTemplatesPath = null)
        {
            _workers = workers;
            _products = products;
            _initialBalances = initialBalances;
            _memory = memory;
            _activityLog = activityLog;
            _reportTemplatesPath = reportTemplatesPath;
        }

        public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GlobalSearchResult>();

            var workersTask = _workers.GetAllWithSkillsAsync();
            var departmentAccountsTask = _workers.GetDepartmentAccountsAsync();
            var productsTask = _products.GetAllWithStagesAsync();
            var balancesTask = _initialBalances.GetAllAsync();
            var activeMemoryTask = _memory.GetActiveAsync();
            var completedMemoryTask = _memory.GetCompletedAsync();
            var activityTask = _activityLog.GetByRangeAsync(
                DateTime.Today.AddDays(-ActivityLogSearchWindowDays), DateTime.Today);

            await Task.WhenAll(
                workersTask, departmentAccountsTask, productsTask, balancesTask,
                activeMemoryTask, completedMemoryTask, activityTask);

            var results = new List<GlobalSearchResult>();
            results.AddRange(MatchWorkers(query, workersTask.Result));
            results.AddRange(MatchDepartmentAccounts(query, departmentAccountsTask.Result));
            results.AddRange(MatchProducts(query, productsTask.Result));
            results.AddRange(MatchStages(query, productsTask.Result));
            results.AddRange(MatchInitialBalances(query, balancesTask.Result));
            results.AddRange(MatchMemoryPlans(query, activeMemoryTask.Result.Concat(completedMemoryTask.Result)));
            results.AddRange(MatchActivityLog(query, activityTask.Result));
            results.AddRange(MatchReportTemplates(query, ReportTemplateStore.Load(_reportTemplatesPath)));

            return results
                .GroupBy(r => r.Category)
                .SelectMany(g => g.OrderByDescending(r => r.Score).Take(MaxResultsPerCategory))
                .OrderByDescending(r => r.Score)
                .ToList();
        }

        // ======================= المطابقة لكل فئة =======================

        private static IEnumerable<GlobalSearchResult> MatchWorkers(string query, IReadOnlyList<Worker> workers)
        {
            foreach (var w in workers)
            {
                var skillsText = string.Join(" ", w.Skills.Select(s => s.ProductionStage.StageName));
                var secondary = string.Join(" ", new[] { skillsText, w.SkillsNotes }.Where(s => !string.IsNullOrWhiteSpace(s)));

                var match = BestMatch(query, (w.FullName, PrimaryFieldWeight), (secondary, SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.Worker,
                    PrimaryText = w.FullName,
                    SecondaryText = string.IsNullOrWhiteSpace(skillsText) ? null : skillsText,
                    Score = match.Value.Score,
                    WorkerId = w.Id
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchDepartmentAccounts(string query, IReadOnlyList<Worker> accounts)
        {
            foreach (var w in accounts)
            {
                var match = BestMatch(query, (w.FullName, PrimaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.DepartmentAccount,
                    PrimaryText = w.FullName,
                    SecondaryText = w.PhoneNumber,
                    Score = match.Value.Score,
                    WorkerId = w.Id
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchProducts(string query, IReadOnlyList<Product> products)
        {
            foreach (var p in products)
            {
                var match = BestMatch(query, (p.Name, PrimaryFieldWeight), (p.Description, SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.Product,
                    PrimaryText = p.Name,
                    SecondaryText = p.Description,
                    Score = match.Value.Score,
                    ProductId = p.Id
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchStages(string query, IReadOnlyList<Product> products)
        {
            // بالاسم بس، مش اسم المنتج — تضمين اسم المنتج كحقل مطابقة تاني
            // كان هيرجّع كل مراحل المنتج مع بعض لمجرد إن المستخدم كتب اسمه،
            // وده تكرار مع نتيجة المنتج نفسها. اسم المنتج للعرض بس (سياق).
            foreach (var p in products)
            {
                foreach (var s in p.Stages)
                {
                    var match = BestMatch(query, (s.StageName, PrimaryFieldWeight));
                    if (match is null) continue;

                    yield return new GlobalSearchResult
                    {
                        Category = SearchCategory.ProductionStage,
                        PrimaryText = s.StageName,
                        SecondaryText = p.Name,
                        Score = match.Value.Score,
                        ProductId = p.Id,
                        ProductionStageId = s.Id
                    };
                }
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchInitialBalances(string query, IReadOnlyList<InitialBalanceDto> balances)
        {
            foreach (var b in balances)
            {
                var secondary = string.Join(" ", new[] { b.Notes, b.ProductName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var match = BestMatch(query, (b.Name, PrimaryFieldWeight), (secondary, SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.InitialBalance,
                    PrimaryText = b.Name,
                    SecondaryText = b.ProductName,
                    Score = match.Value.Score,
                    ProductId = b.ProductId,
                    InitialBalanceId = b.Id
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchMemoryPlans(string query, IEnumerable<ProductionMemoryDto> plans)
        {
            foreach (var m in plans)
            {
                var match = BestMatch(query, (m.ProductName, PrimaryFieldWeight), (m.Notes, SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.MemoryPlan,
                    PrimaryText = m.ProductName,
                    SecondaryText = string.IsNullOrWhiteSpace(m.Notes) ? null : m.Notes,
                    Score = match.Value.Score,
                    ProductId = m.ProductId,
                    MemoryPlanId = m.Id
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchActivityLog(string query, IReadOnlyList<ActivityEvent> events)
        {
            foreach (var e in events)
            {
                var secondary = string.Join(" ", new[] { e.Actor, e.Reason, e.Details }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var match = BestMatch(query, (e.EntityName, PrimaryFieldWeight), (secondary, SecondaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.ActivityLogEntry,
                    PrimaryText = e.EntityName ?? e.EntityType,
                    SecondaryText = $"{e.OccurredAt:yyyy/MM/dd} — {e.Actor}",
                    Score = match.Value.Score,
                    ActivityEventId = e.Id,
                    ActivityEventOccurredAt = e.OccurredAt
                };
            }
        }

        private static IEnumerable<GlobalSearchResult> MatchReportTemplates(string query, IReadOnlyList<ReportTemplate> templates)
        {
            foreach (var t in templates)
            {
                var match = BestMatch(query, (t.Name, PrimaryFieldWeight));
                if (match is null) continue;

                yield return new GlobalSearchResult
                {
                    Category = SearchCategory.ReportTemplate,
                    PrimaryText = t.Name,
                    Score = match.Value.Score,
                    ReportTemplateName = t.Name
                };
            }
        }

        /// <summary>
        /// بيطابق الاستعلام مقابل كذا حقل بأوزان مختلفة (الحقل الأساسي
        /// أهم من الثانوي)، وبيرجّع أعلى نتيجة موزونة بينهم. حقل فاضي/null
        /// بيتجاهل تلقائيًا (SearchMatcher.Match بترجّع null ليه أصلًا).
        ///
        /// **public**: فئتي الإعدادات والدليل (محتوى ثابت في الواجهة، مش
        /// من هنا) بتستخدم نفس الدالة بالظبط عشان الترجيح بين الحقل
        /// الأساسي والثانوي يفضل قاعدة واحدة في كل مكان، مش نسخة تانية
        /// مكتوبة في MainWindow ممكن تنجرف عن الأصل.
        /// </summary>
        public static (SearchMatchKind Kind, int Score)? BestMatch(string query, params (string? Text, double Weight)[] fields)
        {
            SearchMatchKind? bestKind = null;
            var bestScore = 0;

            foreach (var (text, weight) in fields)
            {
                var match = SearchMatcher.Match(query, text);
                if (match is null) continue;

                var weighted = (int)(match.Value.Score * weight);
                if (bestKind is not null && weighted <= bestScore) continue;

                bestScore = weighted;
                bestKind = match.Value.Kind;
            }

            return bestKind is null ? null : (bestKind.Value, bestScore);
        }
    }
}
