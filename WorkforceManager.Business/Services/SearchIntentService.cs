using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// إجابات فورية بالنية لـ"بحث سريع": عبارة زي "غياب سالم" أو "سالم غياب"
    /// بتتفكك لكلمة نية (من قاموس ثابت) + اسم (باقي العبارة)، ولو الاسم
    /// اتطابق مع عامل (أو منتج لنية الإنتاج)، بينادي **سيرفس موجود بالفعل**
    /// ويبني رد مباشر — مفيش رياضة جديدة ولا مصدر تاني لنفس الرقم.
    /// <see cref="ProductionReportService.GetWorkerReportAsync"/> بترجّع كل
    /// حاجة محتاجها 6 من الـ8 نيات (غياب/جزاءات/سلف وحوافز/راتب/مهارات/
    /// إنتاج العامل) في نداء واحد، فده المصدر لكل نيات العامل ما عدا
    /// "تقييم" (WorkerRecognitionService) و"إنتاج"/"متوسط إنتاج" منتج
    /// (DailyProductionReportService).
    ///
    /// ده مش نموذج تعلّم آلي ولا AI — قاموس كلمات ثابت + استدعاء سيرفس
    /// موجود، بالكامل محلي وقابل للشرح خطوة خطوة (شوف <see cref="ParseIntent"/>).
    /// </summary>
    public class SearchIntentService
    {
        private readonly IWorkerRepository _workers;
        private readonly IProductRepository _products;
        private readonly IAttendanceRepository _attendance;
        private readonly ProductionReportService _workerReport;
        private readonly DailyProductionReportService _productReport;
        private readonly WorkerRecognitionService _recognition;
        private readonly WeeklySummaryService _weekly;
        private readonly SkillRatingService _skillRating;

        public SearchIntentService(
            IWorkerRepository workers,
            IProductRepository products,
            IAttendanceRepository attendance,
            ProductionReportService workerReport,
            DailyProductionReportService productReport,
            WorkerRecognitionService recognition,
            WeeklySummaryService weekly,
            SkillRatingService skillRating)
        {
            _workers = workers;
            _products = products;
            _attendance = attendance;
            _workerReport = workerReport;
            _productReport = productReport;
            _recognition = recognition;
            _weekly = weekly;
            _skillRating = skillRating;
        }

        // ======================= قاموس النية (ثوابت مسمّاة، مفيش نصوص متبعثرة) =======================

        /// <summary>
        /// القيم هنا بعد <see cref="ArabicSearch.Normalize"/> بالفعل (مثلًا
        /// "أجر" اتكتبت "اجر" لأن الهمزة بتتوحّد وقت المطابقة) — كل كلمة
        /// وصيغتها المعرّفة بـ"ال" مدرجين صراحة، زي ما التعليمات اقترحت
        /// لكلمات الفترة، بدل منطق تقشير "ال" عام ممكن يجيب نتايج غريبة.
        /// </summary>
        private static readonly Dictionary<string, SearchIntentKind> IntentKeywords = new()
        {
            ["غياب"] = SearchIntentKind.Absence,
            ["الغياب"] = SearchIntentKind.Absence,

            ["جزاء"] = SearchIntentKind.Penalties,
            ["جزاءات"] = SearchIntentKind.Penalties,
            ["الجزاء"] = SearchIntentKind.Penalties,
            ["الجزاءات"] = SearchIntentKind.Penalties,

            ["حوافز"] = SearchIntentKind.Adjustments,
            ["الحوافز"] = SearchIntentKind.Adjustments,
            ["سلف"] = SearchIntentKind.Adjustments,
            ["السلف"] = SearchIntentKind.Adjustments,

            ["راتب"] = SearchIntentKind.Wage,
            ["الراتب"] = SearchIntentKind.Wage,
            ["اجر"] = SearchIntentKind.Wage,
            ["الاجر"] = SearchIntentKind.Wage,

            ["مهارات"] = SearchIntentKind.Skills,
            ["المهارات"] = SearchIntentKind.Skills,

            ["انتاج"] = SearchIntentKind.Production,
            ["الانتاج"] = SearchIntentKind.Production,

            ["تقييم"] = SearchIntentKind.Standing,
            ["التقييم"] = SearchIntentKind.Standing,

            ["احسن"] = SearchIntentKind.TopWorker,
            ["الاحسن"] = SearchIntentKind.TopWorker,
            ["افضل"] = SearchIntentKind.TopWorker,
            ["الافضل"] = SearchIntentKind.TopWorker,

            ["اسوا"] = SearchIntentKind.BottomWorker,
            ["الاسوا"] = SearchIntentKind.BottomWorker,

            ["شغال"] = SearchIntentKind.ProductWorkers,
            ["شغالين"] = SearchIntentKind.ProductWorkers,
            ["الشغالين"] = SearchIntentKind.ProductWorkers,

            // AddStage/AssignSkill: مباشرة هنا (مش placeholder) بس لازمين
            // AddMarkerWords كمان (شوف الحارس في ParseIntent) — "مرحلة دبلة"
            // لوحدها من غير "أضيف" مش نية، عشان محدش يفتكر بحث نصي عادي عن
            // كلمة "مرحلة" بقى فعل إضافة
            ["مرحله"] = SearchIntentKind.AddStage,
            ["المرحله"] = SearchIntentKind.AddStage,

            ["مهاره"] = SearchIntentKind.AssignSkill,
            ["المهاره"] = SearchIntentKind.AssignSkill,
        };

        /// <summary>لازمة مع "مرحلة"/"مهارة" عشان تتحول لفعل حقيقي (AddStage/AssignSkill) — شوف الحارس في ParseIntent</summary>
        private static readonly HashSet<string> AddMarkerWords = new() { "اضيف", "ضيف", "اضافه", "الاضافه" };

        /// <summary>"متوسط" لوحدها مش نية — لازم تترافق مع "انتاج" (بأي ترتيب) عشان تبقى AverageProduction</summary>
        private static readonly HashSet<string> AverageMarkerWords = new() { "متوسط", "المتوسط" };

        /// <summary>
        /// نفس فكرة AverageMarkerWords — لازمة "انتاج" معاها عشان تبقى
        /// TopProduction. "اعلي" مش "اعلى" عمدًا: ArabicSearch.Normalize
        /// بتحوّل ى→ي (زي أي ألف مقصورة تانية)، فـ"أعلى" بترجع "اعلي" فعليًا.
        /// </summary>
        private static readonly HashSet<string> TopMarkerWords = new() { "اعلي", "الاعلي" };

        /// <summary>نفس فكرة AverageMarkerWords — لازمة "انتاج" معاها عشان تبقى BottomProduction</summary>
        private static readonly HashSet<string> BottomMarkerWords = new() { "اقل", "الاقل" };

        private static readonly Dictionary<string, ReportPeriodKind> PeriodKeywords = new()
        {
            ["يوم"] = ReportPeriodKind.Today,
            ["اليوم"] = ReportPeriodKind.Today,
            ["اسبوع"] = ReportPeriodKind.ThisWeek,
            ["الاسبوع"] = ReportPeriodKind.ThisWeek,
            ["شهر"] = ReportPeriodKind.ThisMonth,
            ["الشهر"] = ReportPeriodKind.ThisMonth,
        };

        /// <summary>أيام الأسبوع (بصيغتين) — لنية "إنتاج يوم [مرجع]" بس، شوف TryResolveDayReference</summary>
        private static readonly Dictionary<string, DayOfWeek> WeekdayWords = new()
        {
            ["احد"] = DayOfWeek.Sunday, ["الاحد"] = DayOfWeek.Sunday,
            ["اثنين"] = DayOfWeek.Monday, ["الاثنين"] = DayOfWeek.Monday,
            ["ثلاثاء"] = DayOfWeek.Tuesday, ["الثلاثاء"] = DayOfWeek.Tuesday,
            ["اربعاء"] = DayOfWeek.Wednesday, ["الاربعاء"] = DayOfWeek.Wednesday,
            ["خميس"] = DayOfWeek.Thursday, ["الخميس"] = DayOfWeek.Thursday,
            ["جمعه"] = DayOfWeek.Friday, ["الجمعه"] = DayOfWeek.Friday,
            ["سبت"] = DayOfWeek.Saturday, ["السبت"] = DayOfWeek.Saturday,
        };

        /// <summary>
        /// كلمات حشو بتتشال بس لما نية اتفهمت خلاص (بعد ما IntentKeywords لقت
        /// كلمة نية فعلية) — عشان "مين" أو "اللي" ميفضلوش عالقين في الاسم
        /// المرشّح ويكسروا المطابقة. لا تأثير على بحث عادي (بدون نية) خالص.
        /// </summary>
        /// <summary>
        /// "علي" مش "على" عمدًا (زي TopMarkerWords) — ArabicSearch.Normalize
        /// بتحوّل الألف المقصورة (ى) لياء عادية، فـ"على" بترجع "علي" فعليًا.
        /// </summary>
        private static readonly HashSet<string> FillerWords = new() { "مين", "علي", "اللي", "الي", "فات", "الفات", "ده", "دي", "عامل", "العامل" };

        /// <summary>فرق درجة تصنيف واحد تقريبًا (Prefix=800 لـ Substring=600) — عامل ومنتج بفرق أقل منه يتعادلوا، والعامل ياخد الأولوية</summary>
        private const int NameMatchTieMargin = 50;

        /// <summary>
        /// درجة النتيجة الوحيدة اللي إجابة النية بترجّعها — أعلى من أعلى درجة
        /// ممكنة في SearchMatcher (تطابق دقيق = 1000) عمدًا، عشان نية مقصودة
        /// صراحة من المستخدم تفضل دايمًا فوق أي تخمين نصي عادي في نفس القايمة.
        /// </summary>
        public const int AnswerResultScore = 1100;

        // ======================= التحليل (منطق نقي، بدون DB) =======================

        /// <summary>
        /// بيفكك العبارة لنية + اسم مرشّح، أو null لو العبارة مش نية مدعومة
        /// خالص — وقتها البحث العادي (SearchMatcher على كل الفئات) بيكمل
        /// زي ما هو من غير أي تدخّل. ترتيب الكلمات حر تمامًا (كلمة النية
        /// قبل أو بعد الاسم)، والتحليل حتمي بالكامل: مفيش تخمين احتمالي.
        /// </summary>
        public static ParsedIntentQuery? ParseIntent(string? query) => ParseIntent(query, DateTime.Today);

        /// <summary>
        /// نسخة تاخد "دلوقتي" صراحة — لازمة لاختبار مرجع اليوم (إنتاج يوم
        /// الثلاثاء اللي فات...) من غير الاعتماد على DateTime.Today الحقيقي
        /// وقت الاختبار، نفس مبدأ SearchRankingScorer.ComputeBoost.
        /// </summary>
        public static ParsedIntentQuery? ParseIntent(string? query, DateTime today)
        {
            var normalized = ArabicSearch.Normalize(query);
            if (normalized.Length == 0) return null;

            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            var hasAverageMarker = words.RemoveAll(w => AverageMarkerWords.Contains(w)) > 0;
            var hasTopMarker = words.RemoveAll(w => TopMarkerWords.Contains(w)) > 0;
            var hasBottomMarker = words.RemoveAll(w => BottomMarkerWords.Contains(w)) > 0;
            var hasAddMarker = words.RemoveAll(w => AddMarkerWords.Contains(w)) > 0;

            SearchIntentKind? kind = null;
            for (var i = 0; i < words.Count; i++)
            {
                if (!IntentKeywords.TryGetValue(words[i], out var found)) continue;
                kind = found;
                words.RemoveAt(i);
                break;
            }

            // "متوسط"/"أعلى"/"أقل" من غير كلمة نية معروفة معاها (وبالذات من
            // غير "انتاج") مش نية مدعومة — الثلاثة بنفس القاعدة بالظبط
            if (kind is null) return null;

            if (hasAverageMarker)
            {
                // "متوسط" + أي حاجة غير "انتاج" (مثلًا "متوسط راتب") مش من النيات المتفق عليها
                if (kind != SearchIntentKind.Production) return null;
                kind = SearchIntentKind.AverageProduction;
            }
            else if (hasTopMarker)
            {
                if (kind != SearchIntentKind.Production) return null;
                kind = SearchIntentKind.TopProduction;
            }
            else if (hasBottomMarker)
            {
                if (kind != SearchIntentKind.Production) return null;
                kind = SearchIntentKind.BottomProduction;
            }

            // "مرحلة"/"مهارة" لوحدهم مش فعل — لازم "أضيف"/"ضيف" معاهم، وإلا
            // ده بحث نصي عادي عن الكلمة دي (زي بحث عن مرحلة اسمها "مرحلة
            // التشطيب" مثلًا)، مش نية إضافة
            if (kind is SearchIntentKind.AddStage or SearchIntentKind.AssignSkill && !hasAddMarker)
                return null;

            ReportPeriodKind? period = null;
            for (var i = 0; i < words.Count; i++)
            {
                if (!PeriodKeywords.TryGetValue(words[i], out var foundPeriod)) continue;
                period = foundPeriod;
                words.RemoveAt(i);
                break;
            }

            // مهارات/تقييم مش مرتبطين بفترة أصلًا — لو كلمة فترة اتحطت في
            // العبارة صدفة بتتشال برضه (فوق) عشان متفضلش عالقة في الاسم،
            // بس بتتجاهل هنا (مش هتتبعت لأي سيرفس)
            if (kind is SearchIntentKind.Skills or SearchIntentKind.Standing) period = null;

            // مرجع يوم محدد ("إنتاج يوم الثلاثاء اللي فات") — نية "انتاج" بس،
            // وبيتفحص قبل شيل كلمات الحشو عشان "اللي"/"فات" مايتحسبوش اسم
            DateTime? specificDay = null;
            if (kind == SearchIntentKind.Production)
            {
                for (var i = 0; i < words.Count; i++)
                {
                    if (!TryResolveDayReference(words[i], today, out var resolvedDay)) continue;
                    specificDay = resolvedDay;
                    words.RemoveAt(i);
                    break;
                }
            }

            // "الأسبوع اللي فات" — بتتفحص قبل ما "فات" يتشال كحشو، وبتتجاهل
            // إلا لو النية أحسن/أسوأ عامل (شوف ResolvePastAwarePeriod تحت)
            var hasPastMarker = words.Contains("فات");

            words.RemoveAll(FillerWords.Contains);

            var name = string.Join(' ', words).Trim();

            // "انتاج" + مرجع يوم + مفيش اسم تاني فاضل = إنتاج المصنع كله في
            // يوم بعينه، مش إنتاج عامل/منتج. "انتاج" لوحدها من غير مرجع يوم
            // لسه مش نية (تحت) — الغموض أعلى من كده
            if (kind == SearchIntentKind.Production && specificDay is not null && name.Length == 0)
                return new ParsedIntentQuery { Kind = SearchIntentKind.DayProduction, CandidateName = "", Period = period, SpecificDay = specificDay };

            // "غياب" من غير اسم = مين غايب في المصنع كله، مش غياب عامل بعينه
            if (kind == SearchIntentKind.Absence && name.Length == 0)
                return new ParsedIntentQuery { Kind = SearchIntentKind.DayAbsence, CandidateName = "", Period = period };

            // أعلى/أقل إنتاج مبيسألوش عن حد بالاسم أصلًا — ترتيب عام للمصنع
            // كله. اسم فاضل جوه العبارة (مثلًا "أعلى إنتاج دبلة") مزيج مش
            // مدعوم — نرجع null بدل تخمين المقصود
            if (kind is SearchIntentKind.TopProduction or SearchIntentKind.BottomProduction)
                return name.Length == 0
                    ? new ParsedIntentQuery { Kind = kind.Value, CandidateName = "", Period = period }
                    : null;

            // أحسن/أسوأ عامل بنفس القاعدة بالظبط — بلا اسم، وIsPastPeriod
            // بتتفعّل بس هنا (الوحيدين اللي "فات" ليها معنى فعلي)
            if (kind is SearchIntentKind.TopWorker or SearchIntentKind.BottomWorker)
                return name.Length == 0
                    ? new ParsedIntentQuery { Kind = kind.Value, CandidateName = "", Period = period, IsPastPeriod = hasPastMarker }
                    : null;

            if (name.Length == 0) return null;

            return new ParsedIntentQuery { Kind = kind.Value, CandidateName = name, Period = period };
        }

        /// <summary>
        /// بيحوّل كلمة مرجع يوم ("الثلاثاء"، "امبارح"، "انهارده") لتاريخ فعلي.
        /// أيام الأسبوع دايمًا بترجع أقرب تاريخ **فات** (لو النهارده نفسه نفس
        /// يوم الأسبوع المطلوب، بترجع أسبوع لورا مش النهارده — "الثلاثاء" في
        /// كلام الناس العادي معناها الثلاثاء اللي فات، مش النهارده لو صدفة تلات).
        /// </summary>
        private static bool TryResolveDayReference(string word, DateTime today, out DateTime resolved)
        {
            if (word is "انهارده" or "النهارده") { resolved = today.Date; return true; }
            if (word is "امبارح") { resolved = today.Date.AddDays(-1); return true; }

            if (WeekdayWords.TryGetValue(word, out var targetDay))
            {
                var date = today.Date.AddDays(-1);
                while (date.DayOfWeek != targetDay) date = date.AddDays(-1);
                resolved = date;
                return true;
            }

            resolved = default;
            return false;
        }

        /// <summary>
        /// "الأسبوع/الشهر اللي فات" — مفهوم محلي هنا بس، مش في ReportPeriodKind
        /// المشترك (مستخدم في قوالب التقارير كمان، ومفيش داعي "فترة فاتت"
        /// تبقى خيار هناك لسبب النية دي بس). "اليوم اللي فات" مالوش معنى
        /// واضح فبيتجاهل (بيرجع الفترة الحالية زي ما هي).
        /// </summary>
        private static (DateTime From, DateTime To, string Label) ResolvePastAwarePeriod(ReportPeriodKind kind, bool isPast)
        {
            var (from, to) = ReportPeriod.Resolve(kind);
            if (!isPast) return (from, to, ReportPeriod.Name(kind));

            return kind switch
            {
                ReportPeriodKind.ThisWeek => (from.AddDays(-7), to.AddDays(-7), "الأسبوع اللي فات"),
                // from بتاعة ThisMonth أصلًا أول يوم في الشهر الحالي (ReportPeriod.Resolve)
                ReportPeriodKind.ThisMonth => (from.AddMonths(-1), from.AddDays(-1), "الشهر اللي فات"),
                _ => (from, to, ReportPeriod.Name(kind))
            };
        }

        // ======================= البناء الفعلي (بينادي سيرفس موجود بس) =======================

        public async Task<SearchIntentAnswer?> AnswerAsync(string? query)
        {
            var parsed = ParseIntent(query);
            if (parsed is null) return null;

            // النيات دي بلا اسم أصلًا — مفيش داعي نحاول نطابق عامل/منتج خالص
            if (parsed.Kind == SearchIntentKind.DayProduction) return await BuildDayProductionAnswerAsync(parsed);
            if (parsed.Kind == SearchIntentKind.DayAbsence) return await BuildDayAbsenceAnswerAsync(parsed);
            if (parsed.Kind is SearchIntentKind.TopProduction or SearchIntentKind.BottomProduction)
                return await BuildTopBottomProductionAnswerAsync(parsed);
            if (parsed.Kind is SearchIntentKind.TopWorker or SearchIntentKind.BottomWorker)
                return await BuildWorkerRankingAnswerAsync(parsed);

            // اسم مطلوب بس مش عامل — منتج أو مرحلة، شوف BuildProductWorkersAnswerAsync
            if (parsed.Kind == SearchIntentKind.ProductWorkers)
                return await BuildWhoCanDoAnswerAsync(parsed);

            // فعل — الاسم منتج بس، مفيش داعي نحمّل/نطابق عمال خالص
            if (parsed.Kind == SearchIntentKind.AddStage)
                return await BuildAddStageAnswerAsync(parsed);

            var workers = await _workers.GetAllWithSkillsAsync();
            var (worker, workerScore) = BestWorkerMatch(parsed.CandidateName, workers);

            // فعل — نفس مصدر "غياب"/"راتب" لتحديد العامل، بس مفيش تقرير مطلوب
            if (parsed.Kind == SearchIntentKind.AssignSkill)
                return worker is null ? null : BuildAssignSkillAnswer(worker);

            if (parsed.Kind is not (SearchIntentKind.Production or SearchIntentKind.AverageProduction))
                return worker is null ? null : await BuildWorkerAnswerAsync(parsed, worker);

            // إنتاج/متوسط إنتاج: الاسم ممكن يبقى عامل أو منتج
            var products = await _products.GetAllWithStagesAsync();
            var (product, productScore) = BestProductMatch(parsed.CandidateName, products);

            if (worker is null && product is null) return null;

            var useWorker = worker is not null && (product is null || workerScore + NameMatchTieMargin >= productScore);

            if (useWorker) return await BuildWorkerAnswerAsync(parsed, worker!);

            var (from, to) = ReportPeriod.Resolve(parsed.Period ?? ReportPeriodKind.ThisWeek);
            var productReport = await _productReport.GetForRangeAsync(from, to);
            return BuildProductAnswer(parsed, product!, productReport);
        }

        private static (Worker? Worker, int Score) BestWorkerMatch(string name, IReadOnlyList<Worker> workers)
        {
            Worker? best = null;
            var bestScore = 0;

            foreach (var w in workers)
            {
                var match = GlobalSearchService.BestMatch(name, (w.FullName, GlobalSearchService.PrimaryFieldWeight));
                if (match is null) continue;
                if (best is not null && match.Value.Score <= bestScore) continue;

                best = w;
                bestScore = match.Value.Score;
            }

            return (best, bestScore);
        }

        private static (Product? Product, int Score) BestProductMatch(string name, IReadOnlyList<Product> products)
        {
            Product? best = null;
            var bestScore = 0;

            foreach (var p in products)
            {
                var match = GlobalSearchService.BestMatch(name, (p.Name, GlobalSearchService.PrimaryFieldWeight));
                if (match is null) continue;
                if (best is not null && match.Value.Score <= bestScore) continue;

                best = p;
                bestScore = match.Value.Score;
            }

            return (best, bestScore);
        }

        /// <summary>بالاسم بس (زي GlobalSearchService.MatchStages) — اسم المنتج الأب مش حقل مطابقة</summary>
        private static (ProductionStage? Stage, Product? Parent, int Score) BestStageMatch(string name, IReadOnlyList<Product> products)
        {
            ProductionStage? best = null;
            Product? bestParent = null;
            var bestScore = 0;

            foreach (var p in products)
            {
                foreach (var s in p.Stages)
                {
                    var match = GlobalSearchService.BestMatch(name, (s.StageName, GlobalSearchService.PrimaryFieldWeight));
                    if (match is null) continue;
                    if (best is not null && match.Value.Score <= bestScore) continue;

                    best = s;
                    bestParent = p;
                    bestScore = match.Value.Score;
                }
            }

            return (best, bestParent, bestScore);
        }

        private async Task<SearchIntentAnswer> BuildWorkerAnswerAsync(ParsedIntentQuery parsed, Worker worker)
        {
            if (parsed.Kind == SearchIntentKind.Standing)
                return await BuildStandingAnswerAsync(worker);

            var periodKind = parsed.Period ?? ReportPeriodKind.ThisWeek;
            var (from, to) = ReportPeriod.Resolve(periodKind);
            var report = await _workerReport.GetWorkerReportAsync(worker.Id, from, to);
            var periodLabel = parsed.Kind == SearchIntentKind.Skills ? null : ReportPeriod.Name(periodKind);

            return parsed.Kind switch
            {
                SearchIntentKind.Absence => BuildAbsenceAnswer(worker, report, periodLabel!),
                SearchIntentKind.Penalties => BuildPenaltiesAnswer(worker, report, periodLabel!),
                SearchIntentKind.Adjustments => BuildAdjustmentsAnswer(worker, report, periodLabel!),
                SearchIntentKind.Wage => BuildWageAnswer(worker, report, periodLabel!),
                SearchIntentKind.Skills => BuildSkillsAnswer(worker, report),
                SearchIntentKind.Production => BuildWorkerProductionAnswer(worker, report, periodLabel!),
                SearchIntentKind.AverageProduction => BuildWorkerAverageProductionAnswer(worker, report, periodLabel!),
                _ => throw new InvalidOperationException($"نية غير متوقعة لعامل: {parsed.Kind}")
            };
        }

        private static SearchIntentAnswer BuildAbsenceAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            var noAbsence = r.AbsentWithPermissionDays == 0 && r.AbsentWithoutPermissionDays == 0;

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Absence,
                Title = $"غياب {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                EmptyNote = noAbsence ? "لا يوجد غياب في الفترة دي" : null,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "أيام حضور", Value = $"{r.PresentDays}" },
                    new() { Label = "غياب بإذن", Value = $"{r.AbsentWithPermissionDays} يوم" },
                    new() { Label = "غياب من غير إذن", Value = $"{r.AbsentWithoutPermissionDays} يوم" },
                    new() { Label = "خصم الغياب من اليوميات", Value = $"{r.AbsenceDeduction:0.##} يوم" },
                }
            };
        }

        private static SearchIntentAnswer BuildPenaltiesAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            if (r.Penalties.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.Penalties,
                    Title = $"جزاءات {worker.FullName} — {periodLabel}",
                    WorkerId = worker.Id,
                    Name = worker.FullName,
                    EmptyNote = "لا يوجد جزاءات في الفترة دي"
                };
            }

            var lines = new List<SearchIntentAnswerLine>
            {
                new() { Label = "إجمالي الخصم", Value = $"{r.PenaltyDeduction:0.##} يوم" }
            };
            lines.AddRange(r.Penalties.Select(p => new SearchIntentAnswerLine
            {
                Label = $"{p.Date:yyyy/MM/dd}",
                Value = $"{p.Reason} — {p.DeductionName}"
            }));

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Penalties,
                Title = $"جزاءات {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                Lines = lines
            };
        }

        private static SearchIntentAnswer BuildAdjustmentsAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            if (r.Adjustments.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.Adjustments,
                    Title = $"سلف وحوافز {worker.FullName} — {periodLabel}",
                    WorkerId = worker.Id,
                    Name = worker.FullName,
                    EmptyNote = "لا يوجد سلف أو حوافز في الفترة دي"
                };
            }

            var lines = new List<SearchIntentAnswerLine>
            {
                new() { Label = "إجمالي الحوافز", Value = $"{r.BonusEgp:0.##} جنيه" },
                new() { Label = "إجمالي السلف", Value = $"{r.AdvanceEgp:0.##} جنيه" },
            };
            lines.AddRange(r.Adjustments.Select(a => new SearchIntentAnswerLine
            {
                Label = $"{a.Date:yyyy/MM/dd}",
                Value = $"{a.Type.ToArabicName()} — {a.AmountEgp:0.##} جنيه"
            }));

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Adjustments,
                Title = $"سلف وحوافز {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                Lines = lines
            };
        }

        private static SearchIntentAnswer BuildWageAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            var note = r.HasNoWageRate
                ? "مفيش سعر يومية متسجّل للعامل ده — الأجر هيطلع صفر مهما أنتج"
                : r.IsWageNegative
                    ? "تحذير: السلف أكلت الأجر كله، الصافي طلع بالسالب"
                    : null;

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Wage,
                Title = $"راتب {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                EmptyNote = note,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "صافي اليوميات", Value = $"{r.NetWorkdays:0.##}" },
                    new() { Label = "أجر اليوميات", Value = $"{r.WorkdaysWageEgp:0.##} جنيه" },
                    new() { Label = "الحوافز", Value = $"{r.BonusEgp:0.##} جنيه" },
                    new() { Label = "السلف", Value = $"{r.AdvanceEgp:0.##} جنيه" },
                    new() { Label = "الصافي النهائي", Value = $"{r.NetWageEgp:0.##} جنيه" },
                }
            };
        }

        private static SearchIntentAnswer BuildSkillsAnswer(Worker worker, WorkerProductionReportDto r)
        {
            if (r.Skills.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.Skills,
                    Title = $"مهارات {worker.FullName}",
                    WorkerId = worker.Id,
                    Name = worker.FullName,
                    EmptyNote = "العامل ده لسه من غير أي تقييم مهارة مسجّل"
                };
            }

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Skills,
                Title = $"مهارات {worker.FullName}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                Lines = r.Skills.Select(s => new SearchIntentAnswerLine
                {
                    Label = s.ProductName,
                    Value = $"{s.StarsText} — {s.StagesText}"
                }).ToList()
            };
        }

        private static SearchIntentAnswer BuildWorkerProductionAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Production,
                Title = $"إنتاج {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                EmptyNote = r.TotalPieces == 0 ? "لا يوجد إنتاج مسجّل للعامل ده في الفترة دي" : null,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "قطع منتجة", Value = $"{r.TotalPieces:N0}" },
                    new() { Label = "يوميات منتجة", Value = $"{r.ProducedWorkdays:0.##}" },
                }
            };
        }

        private static SearchIntentAnswer BuildWorkerAverageProductionAnswer(Worker worker, WorkerProductionReportDto r, string periodLabel)
        {
            if (r.ProducedWorkdays <= 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.AverageProduction,
                    Title = $"متوسط إنتاج {worker.FullName} — {periodLabel}",
                    WorkerId = worker.Id,
                    Name = worker.FullName,
                    EmptyNote = "لا يوجد إنتاج مسجّل في الفترة دي لحساب المتوسط"
                };
            }

            var average = r.TotalPieces / r.ProducedWorkdays;
            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.AverageProduction,
                Title = $"متوسط إنتاج {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "متوسط الإنتاج لكل يومية", Value = $"{average:0.##} قطعة" },
                }
            };
        }

        private static SearchIntentAnswer BuildProductAnswer(
            ParsedIntentQuery parsed, Product product, DailyProductionReportDto report)
        {
            var periodKind = parsed.Period ?? ReportPeriodKind.ThisWeek;
            var periodLabel = ReportPeriod.Name(periodKind);
            var row = report.Products.FirstOrDefault(p => p.ProductId == product.Id);

            if (parsed.Kind == SearchIntentKind.AverageProduction)
            {
                var (from, to) = ReportPeriod.Resolve(periodKind);
                var dayCount = (to.Date - from.Date).Days + 1;

                if (row is null || dayCount <= 0)
                {
                    return new SearchIntentAnswer
                    {
                        Kind = SearchIntentKind.AverageProduction,
                        Title = $"متوسط إنتاج {product.Name} — {periodLabel}",
                        ProductId = product.Id,
                    Name = product.Name,
                        EmptyNote = "لا يوجد إنتاج مسجّل للمنتج ده في الفترة دي لحساب المتوسط"
                    };
                }

                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.AverageProduction,
                    Title = $"متوسط إنتاج {product.Name} — {periodLabel}",
                    ProductId = product.Id,
                    Name = product.Name,
                    Lines = new List<SearchIntentAnswerLine>
                    {
                        new() { Label = "متوسط التام يوميًا", Value = $"{row.CompletedPieces / (decimal)dayCount:0.##} قطعة" },
                    }
                };
            }

            if (row is null)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.Production,
                    Title = $"إنتاج {product.Name} — {periodLabel}",
                    ProductId = product.Id,
                    Name = product.Name,
                    EmptyNote = "لا يوجد إنتاج مسجّل للمنتج ده في الفترة دي"
                };
            }

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Production,
                Title = $"إنتاج {product.Name} — {periodLabel}",
                ProductId = product.Id,
                    Name = product.Name,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "تم إنتاجه (تام)", Value = $"{row.CompletedPieces:N0} قطعة" },
                    new() { Label = "دخل الخط", Value = $"{row.StartedPieces:N0} قطعة" },
                    new() { Label = "هالك", Value = $"{row.ScrapPieces:N0} قطعة" },
                }
            };
        }

        private async Task<SearchIntentAnswer> BuildDayProductionAnswerAsync(ParsedIntentQuery parsed)
        {
            var day = parsed.SpecificDay!.Value;
            var report = await _productReport.GetAsync(day);
            var dayLabel = $"{ArabicDayName(day.DayOfWeek)} ({day:yyyy/MM/dd})";

            var noActivity = report.TotalCompletedPieces == 0 && report.TotalStartedPieces == 0;

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.DayProduction,
                Title = $"إنتاج يوم {dayLabel}",
                EmptyNote = noActivity ? "لا يوجد إنتاج مسجّل في اليوم ده" : null,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "تم إنتاجه (تام)", Value = $"{report.TotalCompletedPieces:N0} قطعة" },
                    new() { Label = "دخل الخط", Value = $"{report.TotalStartedPieces:N0} قطعة" },
                    new() { Label = "هالك", Value = $"{report.TotalScrapPieces:N0} قطعة" },
                }
            };
        }

        private static string ArabicDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            DayOfWeek.Saturday => "السبت",
            _ => day.ToString()
        };

        /// <summary>
        /// مين غايب في المصنع كله لفترة معينة — عكس Absence (بتاعة عامل واحد
        /// بالاسم)، هنا بلا اسم خالص. الافتراضي عند غياب كلمة فترة **النهارده**
        /// مش الأسبوع (عكس باقي النيات) — "غياب" لوحدها سؤال عن دلوقتي
        /// طبيعي أكتر من سؤال عن أسبوع كامل.
        /// </summary>
        private async Task<SearchIntentAnswer> BuildDayAbsenceAnswerAsync(ParsedIntentQuery parsed)
        {
            var periodKind = parsed.Period ?? ReportPeriodKind.Today;
            var (from, to) = ReportPeriod.Resolve(periodKind);
            var periodLabel = ReportPeriod.Name(periodKind);

            var records = await _attendance.GetByRangeAsync(from, to);
            var absentRecords = records.Where(a => a.IsAbsence).ToList();

            if (absentRecords.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.DayAbsence,
                    Title = $"غياب — {periodLabel}",
                    EmptyNote = "لا يوجد غياب في الفترة دي"
                };
            }

            var workers = await _workers.GetAllWithSkillsAsync();
            var nameById = workers.ToDictionary(w => w.Id, w => w.FullName);

            var lines = absentRecords
                // حسابات الأقسام مستبعدة من GetAllWithSkillsAsync أصلًا — نفس
                // استبعادها من كل مكان تاني في البحث الشامل
                .Where(a => nameById.ContainsKey(a.WorkerId))
                .GroupBy(a => a.WorkerId)
                .Select(g => new
                {
                    Name = nameById[g.Key],
                    Total = g.Count(),
                    Unexcused = g.Count(a => a.Status == AttendanceStatus.AbsentWithoutPermission)
                })
                .OrderByDescending(x => x.Total).ThenBy(x => x.Name)
                .Select(x => new SearchIntentAnswerLine
                {
                    Label = x.Name,
                    Value = x.Unexcused > 0
                        ? $"{x.Total} يوم غياب ({x.Unexcused} من غير إذن)"
                        : $"{x.Total} يوم غياب (بإذن)"
                })
                .ToList();

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.DayAbsence,
                Title = $"غياب — {periodLabel}",
                Lines = lines
            };
        }

        /// <summary>
        /// "أعلى/أقل إنتاج" بلا اسم — بطاقة واحدة فيها **الاتنين مع بعض**
        /// (أعلى/أقل منتج + أعلى/أقل عامل)، عشان مفيش داعي المستخدم يحدد
        /// أي نوع يقصد. المنتج من DailyProductionReportService (نفس مصدر
        /// "إنتاج [منتج]" العادية)، والعامل من
        /// ProductionReportService.GetGeneralReportAsync.ByWorker (**مش**
        /// GetWorkerReportAsync، دي لعامل واحد بس) — بنعيد ترتيبها بالقطع
        /// هنا، الترتيب الافتراضي جوه السيرفس نفسه باليوميات مش القطع.
        /// عمال الساعة ومن غير إنتاج قطع مستبعدين من ترتيب العمال (نفس
        /// استبعاد WorkerRecognitionRules.Rank) — "أقل عامل إنتاجًا" معناها
        /// أضعف من بيشتغل بالقطعة فعلًا، مش حساب قسم أو عامل غايب تمامًا.
        /// </summary>
        private async Task<SearchIntentAnswer> BuildTopBottomProductionAnswerAsync(ParsedIntentQuery parsed)
        {
            var isTop = parsed.Kind == SearchIntentKind.TopProduction;
            var periodKind = parsed.Period ?? ReportPeriodKind.ThisWeek;
            var (from, to) = ReportPeriod.Resolve(periodKind);
            var periodLabel = ReportPeriod.Name(periodKind);
            var title = isTop ? $"أعلى إنتاج — {periodLabel}" : $"أقل إنتاج — {periodLabel}";

            var productReport = await _productReport.GetForRangeAsync(from, to);
            var productRow = isTop
                ? productReport.Products.OrderByDescending(p => p.CompletedPieces).FirstOrDefault()
                : productReport.Products.OrderBy(p => p.CompletedPieces).FirstOrDefault();

            var generalReport = await _workerReport.GetGeneralReportAsync(from, to);
            var eligibleWorkers = generalReport.ByWorker.Where(w => !w.IsHourly && w.TotalPieces > 0);
            var workerRow = isTop
                ? eligibleWorkers.OrderByDescending(w => w.TotalPieces).FirstOrDefault()
                : eligibleWorkers.OrderBy(w => w.TotalPieces).FirstOrDefault();

            if (productRow is null && workerRow is null)
            {
                return new SearchIntentAnswer
                {
                    Kind = parsed.Kind,
                    Title = title,
                    EmptyNote = "لا يوجد إنتاج مسجّل في الفترة دي"
                };
            }

            var lines = new List<SearchIntentAnswerLine>();
            if (productRow is not null)
            {
                lines.Add(new SearchIntentAnswerLine
                {
                    Label = isTop ? "أعلى منتج" : "أقل منتج",
                    Value = $"{productRow.ProductName} — {productRow.CompletedPieces:N0} قطعة"
                });
            }
            if (workerRow is not null)
            {
                lines.Add(new SearchIntentAnswerLine
                {
                    Label = isTop ? "أعلى عامل" : "أقل عامل",
                    Value = $"{workerRow.WorkerName} — {workerRow.TotalPieces:N0} قطعة"
                });
            }

            return new SearchIntentAnswer
            {
                Kind = parsed.Kind,
                Title = title,
                // المنتج الافتراضي للهبوط عند الدوسة — قرار بسيط، الاستعلام
                // أصلًا عن الإنتاج مش عن عامل بعينه
                ProductId = productRow?.ProductId,
                Lines = lines
            };
        }

        /// <summary>
        /// "أحسن/أسوأ عامل" بلا اسم — ترتيب الفريق كله بنفس القاعدة اللي
        /// "أحسن 3 عمال" الحقيقية شغالة بيها (WorkerRecognitionRules.Rank)،
        /// بس من غير قص لأول 3 — ComputeWeeklyTopAsync/ComputeMonthlyTopAsync
        /// الموجودين بيقصّوا القايمة قبل ما ترجع، فمفيش طريقة توصل لآخرها
        /// (أسوأ عامل) من غيرهم؛ هنا بننادي نفس اللبنات (WeeklySummaryService
        /// + WorkerRecognitionRules.Rank) واحنا اللي بناخد أول/آخر عنصر.
        /// **"أسوأ عامل" حاجة جديدة فعلًا** — مفيش نسخة قديمة أو حالية منها
        /// في البرنامج، طلب صريح من المستخدم رغم حساسية الصياغة.
        /// </summary>
        private async Task<SearchIntentAnswer> BuildWorkerRankingAnswerAsync(ParsedIntentQuery parsed)
        {
            var isTop = parsed.Kind == SearchIntentKind.TopWorker;
            var (from, to, periodLabel) = ResolvePastAwarePeriod(parsed.Period ?? ReportPeriodKind.ThisWeek, parsed.IsPastPeriod);
            var title = isTop ? $"أحسن عامل — {periodLabel}" : $"أسوأ عامل — {periodLabel}";

            var team = await _weekly.GetTeamSummaryForRangeAsync(from, to);
            var difficulty = await _weekly.LoadDifficultyByStageIdAsync();
            var ranked = WorkerRecognitionRules.Rank(team, difficulty);

            var chosen = ranked.Count == 0 ? null : (isTop ? ranked[0] : ranked[^1]);

            if (chosen is null)
            {
                return new SearchIntentAnswer
                {
                    Kind = parsed.Kind,
                    Title = title,
                    EmptyNote = "مفيش عمال مؤهلين للمقارنة في الفترة دي (ساعي/حساب قسم/مفيش إنتاج كفاية)"
                };
            }

            return new SearchIntentAnswer
            {
                Kind = parsed.Kind,
                Title = title,
                WorkerId = chosen.WorkerId,
                Name = chosen.WorkerName,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "الترتيب", Value = $"{(isTop ? 1 : ranked.Count)} من {ranked.Count}" },
                    new() { Label = "إجمالي القطع", Value = $"{chosen.TotalPieces:N0}" },
                    new() { Label = "صافي اليوميات", Value = $"{chosen.NetWorkdays:0.##}" },
                    new() { Label = "درجة الترتيب", Value = $"{WorkerRecognitionRules.RecognitionScore(chosen, difficulty):0.##}" },
                }
            };
        }

        /// <summary>
        /// "مين شغال على [منتج أو مرحلة]" — الاسم ممكن يبقى منتج أو مرحلة،
        /// نفس مبدأ عامل/منتج في نية الإنتاج (أعلى درجة تطابق بتكسب، بدون
        /// أفضلية افتراضية للاتنين هنا — عكس عامل/منتج، مفيش سبب واحد
        /// يستاهل يبقى افتراضي).
        /// </summary>
        private async Task<SearchIntentAnswer?> BuildWhoCanDoAnswerAsync(ParsedIntentQuery parsed)
        {
            var products = await _products.GetAllWithStagesAsync();
            var (product, productScore) = BestProductMatch(parsed.CandidateName, products);
            var (stage, stageParent, stageScore) = BestStageMatch(parsed.CandidateName, products);

            if (product is null && stage is null) return null;

            return stage is not null && (product is null || stageScore > productScore)
                ? await BuildStageWorkersAnswerAsync(stage, stageParent!)
                : await BuildProductWorkersAnswerAsync(product!);
        }

        /// <summary>
        /// عمال منتج كامل — IWorkerRepository.GetSkillsForProductAsync بيرجّع
        /// صف لكل (عامل، مرحلة) مؤهل ليها، فعامل مؤهل لأكتر من مرحلة في نفس
        /// المنتج بيتكرر؛ SkillRatingService.Rank بترتب الكل بالنجوم، وبعدين
        /// بناخد أول ظهور لكل عامل (نجومه الأعلى) عشان الاسم يظهر مرة واحدة.
        /// </summary>
        private async Task<SearchIntentAnswer> BuildProductWorkersAnswerAsync(Product product)
        {
            var skills = await _workers.GetSkillsForProductAsync(product.Id);
            var title = $"مين شغال على {product.Name}";

            if (skills.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.ProductWorkers,
                    Title = title,
                    ProductId = product.Id,
                    Name = product.Name,
                    EmptyNote = "مفيش عامل مؤهل للمنتج ده لسه"
                };
            }

            var lines = SkillRatingService.Rank(skills)
                .GroupBy(s => s.WorkerId)
                .Select(g => g.First())
                .Select(s => new SearchIntentAnswerLine
                {
                    Label = s.Worker.FullName,
                    Value = $"{RtlSafeText.Stars(s.Stars)} — {s.ProductionStage.StageName}"
                })
                .ToList();

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.ProductWorkers,
                Title = title,
                ProductId = product.Id,
                Name = product.Name,
                Lines = lines
            };
        }

        /// <summary>عمال مرحلة واحدة — نفس مصدر "مين يعرف يعمل المرحلة دي" الموجود في شاشة المنتجات بالظبط</summary>
        private async Task<SearchIntentAnswer> BuildStageWorkersAnswerAsync(ProductionStage stage, Product parent)
        {
            var ranked = await _skillRating.GetRankedForStageAsync(stage.Id);
            var title = $"مين بيعرف {stage.StageName} ({parent.Name})";

            if (ranked.Count == 0)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.ProductWorkers,
                    Title = title,
                    ProductId = parent.Id,
                    Name = stage.StageName,
                    EmptyNote = "مفيش عامل مؤهل للمرحلة دي لسه"
                };
            }

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.ProductWorkers,
                Title = title,
                ProductId = parent.Id,
                Name = stage.StageName,
                Lines = ranked.Select(r => new SearchIntentAnswerLine { Label = r.WorkerName, Value = r.StarsText }).ToList()
            };
        }

        /// <summary>
        /// "أضيف مرحلة" — فعل، مش إجابة. بتحدد المنتج بس؛ **مفيش نداء
        /// لـ ProductManagementService.AddStageAsync هنا خالص** — Business
        /// ممنوع يفتح ديالوج WPF، فالكتابة الفعلية بتحصل بعد ما المستخدم
        /// يدوس على النتيجة ويراجع StageEditDialog ويحفظ بنفسه (شوف الهبوط
        /// في MainWindow.LandOnSearchResultAsync).
        /// </summary>
        private async Task<SearchIntentAnswer?> BuildAddStageAnswerAsync(ParsedIntentQuery parsed)
        {
            var products = await _products.GetAllWithStagesAsync();
            var (product, _) = BestProductMatch(parsed.CandidateName, products);
            if (product is null) return null;

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.AddStage,
                Title = $"أضيف مرحلة — {product.Name}",
                Name = product.Name,
                ProductId = product.Id
            };
        }

        /// <summary>
        /// "أضيف مهارة" — فعل، مش إجابة. بتحدد العامل بس (مفيش تقرير
        /// مطلوب فمفيش نداء DB إضافي هنا)؛ **مفيش نداء لـ
        /// WorkerManagementService.AssignSkillAsync هنا خالص**، نفس سبب
        /// BuildAddStageAnswerAsync بالظبط — الكتابة الفعلية بتحصل بعد ما
        /// المستخدم يفتح "وضع الإضافة" على كارت العامل ويختار المرحلة ويحفظ.
        /// </summary>
        private static SearchIntentAnswer BuildAssignSkillAnswer(Worker worker) => new()
        {
            Kind = SearchIntentKind.AssignSkill,
            Title = $"أضيف مهارة — {worker.FullName}",
            Name = worker.FullName,
            WorkerId = worker.Id
        };

        private async Task<SearchIntentAnswer> BuildStandingAnswerAsync(Worker worker)
        {
            var explanation = await _recognition.GetWeeklyExplanationAsync(worker.Id, DateTime.Today);
            var periodLabel = ReportPeriod.Name(ReportPeriodKind.ThisWeek);

            if (explanation is null)
            {
                return new SearchIntentAnswer
                {
                    Kind = SearchIntentKind.Standing,
                    Title = $"تقييم {worker.FullName} — {periodLabel}",
                    WorkerId = worker.Id,
                    Name = worker.FullName,
                    EmptyNote = "العامل ده مش داخل مقارنة ترتيب الأسبوع ده (ساعي/حساب قسم/مفيش إنتاج كفاية)"
                };
            }

            return new SearchIntentAnswer
            {
                Kind = SearchIntentKind.Standing,
                Title = $"تقييم {worker.FullName} — {periodLabel}",
                WorkerId = worker.Id,
                    Name = worker.FullName,
                Lines = new List<SearchIntentAnswerLine>
                {
                    new() { Label = "الترتيب", Value = $"{explanation.Rank} من {explanation.EligibleWorkerCount}" },
                    new() { Label = "إجمالي القطع", Value = $"{explanation.TotalPieces:N0}" },
                    new() { Label = "يوميات معدّلة (بعد تنوّع المراحل)", Value = $"{explanation.AdjustedWorkdays:0.##}" },
                    new() { Label = "درجة الترتيب النهائية", Value = $"{explanation.FinalScore:0.##}" },
                }
            };
        }
    }
}
