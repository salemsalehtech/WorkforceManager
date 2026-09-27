using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقييم مهارة العامل بالنجوم — المكان الوحيد اللي بيحسب ويرتّب.
    ///
    /// الفكرة الأساسية: **رقمين منفصلين، مش رقم واحد**.
    ///
    ///   ⭐ النجوم (1–5)  = رأي المدير. هو اللي بيحطها والنظام مبيلمسهاش.
    ///   📊 الأداء المقاس = إنتاجه الفعلي ÷ الكوتة. النظام بيحسبه ويعرضه
    ///      جنب النجوم كمعلومة، بس **مبيقترحش تعديل بناءً عليه**.
    ///
    /// المراجعة الشهرية (<see cref="BuildReviewAsync"/>) بتقترح رفع
    /// نجوم عامل على مهاراته القديمة لما يكتسب مهارات جديدة في نفس
    /// الفترة — إشارة تطوّر حقيقية (بيتعلّم مراحل تانية)، مش عدد
    /// الضربات اللي بيضربها على مرحلة عارفها بالفعل. الاقتراح دايمًا
    /// رفع، مش نزول — تقييم عامل لأسفل قرار المدير وحده من غير أي
    /// اقتراح تلقائي.
    ///
    /// ليه الفصل ده أصلاً: المدير شايف حاجات الأرقام مبتقولهاش — ماكينة
    /// بايظة، شغل صعب الشهر ده، عامل جديد لسه بيتعلّم. لو النظام غيّر
    /// التقييم لوحده، المدير هيلاقي أرقام بتتقلب من ورا ظهره وهيبطّل
    /// يثق فيها. فبدل كده: النظام بيقترح، والقرار يفضل للمدير.
    /// </summary>
    public class SkillRatingService
    {
        // ------- ثوابت القياس -------

        /// <summary>الفترة اللي القياس بيبص عليها</summary>
        public const int LookbackDays = 30;

        /// <summary>أقل عدد أيام شغل قبل ما القياس يبقى له معنى</summary>
        public const int MinSampleDays = 3;

        /// <summary>كل قد إيه المدير يراجع التقييمات (بالأيام)</summary>
        public const int ReviewIntervalDays = 30;

        /// <summary>النجوم الافتراضية = بيعمل الكوتة بالظبط</summary>
        public const int DefaultStars = 3;

        /// <summary>
        /// حدود تحويل الأداء المقاس لنجوم مقترحة.
        ///
        /// الحدود متباعدة عن قصد: فرق 5% في الإنتاج مش سبب كافي إن
        /// تقييم عامل يتغيّر. النجمة الواحدة لازم تعني فرق حقيقي.
        /// </summary>
        private static readonly (decimal MinRatio, int Stars)[] StarBands =
        {
            (1.35m, 5),   // بيعمل الكوتة وزيادة 35%+ — ممتاز
            (1.15m, 4),   // فوق الكوتة بوضوح
            (0.85m, 3),   // بيعمل الكوتة تقريبًا
            (0.70m, 2),   // تحت الكوتة
            (0m,    1)    // بعيد عن الكوتة
        };

        private readonly IWorkerSkillRepository _skills;
        private readonly IDailyProductionRepository _production;
        private readonly IWorkerRepository _workers;
        private readonly CurrentUserContext _currentUser;
        private readonly ActivityLogService _log;

        public SkillRatingService(
            IWorkerSkillRepository skills,
            IDailyProductionRepository production,
            IWorkerRepository workers,
            CurrentUserContext currentUser,
            ActivityLogService log)
        {
            _skills = skills;
            _production = production;
            _workers = workers;
            _currentUser = currentUser;
            _log = log;
        }

        // ======================= قواعد نقية =======================

        /// <summary>النجوم اللي الأداء المقاس ده بيستاهلها</summary>
        public static int StarsForRatio(decimal ratio) =>
            StarBands.First(band => ratio >= band.MinRatio).Stars;

        /// <summary>وصف النجوم بالعربي — نص واحد في البرنامج كله</summary>
        public static string StarsLabel(int stars) => stars switch
        {
            5 => "ممتاز",
            4 => "كويس جدًا",
            3 => "عادي",
            2 => "ضعيف",
            _ => "ضعيف جدًا"
        };

        /// <summary>المستوى القديم المقابل للنجوم — عشان العمودين ميتناقضوش</summary>
        public static SkillLevel LevelForStars(int stars) => stars switch
        {
            >= 4 => SkillLevel.Expert,
            3 => SkillLevel.Proficient,
            _ => SkillLevel.Beginner
        };

        /// <summary>
        /// متوسط نجوم العامل على منتج = متوسط المراحل **اللي هو مربوط
        /// بيها فعلاً** في المنتج ده.
        ///
        /// المراحل اللي مالوش فيها مهارة مبتتحسبش صفر عن قصد: العامل
        /// المتخصص في 3 مراحل من 11 مش ضعيف، هو متخصص — وحسابه صفر في
        /// الباقي كان هيخلي كل المتخصصين يبانوا سيئين.
        ///
        /// بيرجّع null لو مالوش أي مهارة في المنتج ده.
        /// </summary>
        public static decimal? ProductStars(IEnumerable<WorkerSkill> skillsOnProduct)
        {
            var values = skillsOnProduct.Select(s => (decimal)s.Stars).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }

        /// <summary>
        /// الأداء المقاس من سجلات مرحلة واحدة، ومعاه عدد الأيام.
        /// دالة نقية — دي اللي بتخلي الحساب قابل للشرح والاختبار.
        ///
        /// بترجّع null لو الأيام أقل من الحد الأدنى.
        /// </summary>
        public static (decimal Ratio, int Days)? MeasureFromRecords(IEnumerable<DailyProduction> stageRecords)
        {
            // اليوم الواحد ممكن يكون فيه أكتر من سجل على نفس المرحلة،
            // والقياس بيتم على اليوم مش على السجل: عامل اتسجل له سجلين
            // نص كوتة كل واحد عمل كوتة كاملة، مش نص كوتة مرتين
            var perDay = stageRecords
                .Where(r => r.PiecesPerWorkdayAtEntry > 0)
                .GroupBy(r => r.Date.Date)
                .Select(g => (decimal)g.Sum(r => r.PieceCount) / g.First().PiecesPerWorkdayAtEntry)
                .ToList();

            if (perDay.Count < MinSampleDays) return null;

            return (Math.Round(perDay.Average(), 2), perDay.Count);
        }

        /// <summary>
        /// نفس MeasureFromRecords، بس لزوج (عامل، مرحلة) واحد بس — استعلام
        /// واحد على سجلات العامل ده (مفهرس)، مفلتر بالمرحلة في الميموري،
        /// مش مسح كل الجدول زي MeasureAllAsync. للحظة "عامل اتحدد للمرحلة
        /// دي دلوقتي" (شوف TryGrowAutoAddedSkillAsync)، مش للمراجعة
        /// الشهرية الجماعية.
        /// </summary>
        public async Task<WorkerSkill?> MeasureOneAsync(int workerId, int stageId, DateTime asOf)
        {
            var skill = await _skills.GetAsync(workerId, stageId);
            if (skill is null) return null;

            var from = asOf.Date.AddDays(-LookbackDays);
            var records = (await _production.GetByWorkerAndRangeAsync(workerId, from, asOf.Date))
                .Where(r => r.ProductionStageId == stageId)
                .ToList();

            var sample = MeasureFromRecords(records);
            if (sample is not null)
            {
                skill.MeasuredRatio = sample.Value.Ratio;
                skill.MeasuredDays = sample.Value.Days;
                skill.MeasuredAt = DateTime.Now;
                _skills.Update(skill);
                await _skills.SaveChangesAsync();
            }

            return skill;
        }

        // ======================= رأي المدير =======================

        /// <summary>
        /// يحطّ نجوم المدير على مهارة. ده الطريق **الوحيد** اللي بيغيّر
        /// النجوم — النظام مالوش أي دالة بتكتب فيها.
        /// </summary>
        public async Task SetStarsAsync(int workerId, int stageId, int stars)
        {
            if (stars is < 1 or > 5)
                throw new InvalidOperationException("التقييم لازم يكون من نجمة لـ 5 نجوم");

            var skill = await _skills.GetAsync(workerId, stageId)
                ?? throw new InvalidOperationException("العامل ده مش مربوط بالمرحلة دي");

            skill.Stars = stars;
            skill.Level = LevelForStars(stars);
            skill.StarsUpdatedAt = DateTime.Now;
            skill.StarsUpdatedBy = _currentUser.ActorName;

            _skills.Update(skill);
            await _skills.SaveChangesAsync();
        }

        // ======================= قياس النظام =======================

        /// <summary>
        /// يقيس أداء كل العمال على كل مهاراتهم من الإنتاج الفعلي.
        ///
        /// **مبيغيّرش أي نجوم** — بيحدّث رقم القياس بس، وده اللي
        /// المراجعة الشهرية بتقارن بيه.
        ///
        /// **بيمسح القياس القديم كمان** لو مهارة كان ليها قياس فعلي قبل
        /// كده وبقى مفيش وراه عينة كفاية دلوقتي (إنتاجه اتشال كله أو
        /// جزء كبير منه) — من غيره القياس بيفضل شبح: مرحلة كان ليها
        /// قياس من شهر فات وإنتاجها اتشال بالكامل بعدين كانت هتفضل
        /// عارضة نفس الرقم القديم للأبد، لأن الحلقة الأساسية بتمر على
        /// المراحل اللي ليها إنتاج **حاليًا** بس.
        /// </summary>
        /// <returns>عدد المهارات اللي اتقاس أداؤها من جديد</returns>
        public async Task<int> MeasureAllAsync(DateTime asOf)
        {
            var from = asOf.Date.AddDays(-LookbackDays);
            var records = await _production.GetByRangeAsync(from, asOf.Date);

            var byWorkerStage = records
                .GroupBy(r => (r.WorkerId, r.ProductionStageId))
                .ToDictionary(g => g.Key, g => g.ToList());

            // كل المهارات اللي ليها قياس قديم — عشان لو عينتها الحالية
            // بقت غير كافية (أو اختفت خالص) نمسح القياس القديم بدل ما
            // نسيبه شبح. مضمومة مع مفاتيح الإنتاج الحالي عشان أي مهارة
            // من الاتنين تتراجع بنفس المنطق في حلقة واحدة
            var previouslyMeasuredKeys = (await _skills.FindAsync(s => s.MeasuredAt != null))
                .Select(s => (s.WorkerId, s.ProductionStageId));

            var keysToProcess = previouslyMeasuredKeys
                .Union(byWorkerStage.Keys)
                .Distinct()
                .ToList();

            // استعلام واحد لكل مهارات العمال المعنيين، بدل نداء GetAsync
            // منفصل لكل (عامل، مرحلة) — كانت N+1 هنا، وده بيتنفذ في كل
            // مراجعة شهرية على كل عمال المصنع
            var relevantWorkerIds = keysToProcess.Select(k => k.WorkerId).Distinct().ToList();
            var skillsByKey = (await _skills.FindAsync(s => relevantWorkerIds.Contains(s.WorkerId)))
                .ToDictionary(s => (s.WorkerId, s.ProductionStageId));

            var measured = 0;
            var cleared = 0;
            foreach (var (workerId, stageId) in keysToProcess)
            {
                if (!skillsByKey.TryGetValue((workerId, stageId), out var skill)) continue;

                var sample = byWorkerStage.TryGetValue((workerId, stageId), out var stageRecords)
                    ? MeasureFromRecords(stageRecords)
                    : null;

                if (sample is not null)
                {
                    skill.MeasuredRatio = sample.Value.Ratio;
                    skill.MeasuredDays = sample.Value.Days;
                    skill.MeasuredAt = DateTime.Now;
                    _skills.Update(skill);
                    measured++;
                }
                else if (skill.MeasuredAt is not null)
                {
                    skill.MeasuredRatio = 1.0m;
                    skill.MeasuredDays = 0;
                    skill.MeasuredAt = null;
                    _skills.Update(skill);
                    cleared++;
                }
            }

            if (measured > 0 || cleared > 0) await _skills.SaveChangesAsync();
            return measured;
        }

        /// <summary>
        /// نمو تلقائي بعد كل اختيار — بتتنادى بعد كل تسجيل/نقل إنتاج
        /// (ProductionFlowService.RecordFlowAsync، WorkdayCalculationService.
        /// UpdateProductionAsync) على مهارة اتضافت تلقائيًا بس
        /// (<see cref="WorkerSkill.IsAutoAdded"/>) — مهارة يدوية مبتتلمسش
        /// خالص، حتى لو المدير عدّل نجومها بعد كده (قرار اتأكّد مع
        /// المستخدم: IsAutoAdded بيفضل true للأبد، مش بيرجع false).
        ///
        /// القياس بيتحدّث الأول (MeasureOneAsync) عشان الصيغة تشتغل على
        /// أحدث بيانات، وبعدين SkillGrowthCalculator النقية هي اللي
        /// بتقرر — الخدمة دي بس بتجيب المدخلات وتحفظ النتيجة وتسجّلها.
        /// </summary>
        public async Task TryGrowAutoAddedSkillAsync(int workerId, int stageId, DateTime asOf)
        {
            var skill = await MeasureOneAsync(workerId, stageId, asOf);
            if (skill is null || !skill.IsAutoAdded) return;

            var timesSelected = await _production.CountByWorkerAndStageAsync(workerId, stageId);
            var measuredRatio = skill.MeasuredDays >= MinSampleDays ? (decimal?)skill.MeasuredRatio : null;
            var newStars = SkillGrowthCalculator.ComputeNextStars(skill.Stars, timesSelected, measuredRatio);

            if (newStars <= skill.Stars) return;

            var oldStars = skill.Stars;
            skill.Stars = newStars;
            _skills.Update(skill);
            await _skills.SaveChangesAsync();

            var worker = await _workers.GetByIdAsync(workerId);
            await _log.LogAsync(
                ActivityEventType.SkillAutoAdjusted, nameof(WorkerSkill), skill.Id,
                entityName: worker?.FullName,
                details: $"{oldStars} → {newStars}★ بعد {timesSelected} اختيار، متوسط إنتاج {skill.MeasuredRatio:0.00}");
        }

        /// <summary>
        /// نسخة مجمّعة من <see cref="TryGrowAutoAddedSkillAsync"/> لعدة أزواج
        /// (عامل، مرحلة) مرة واحدة — نفس المنطق بالحرف، لكن استعلامات
        /// المهارات وإنتاج كل عامل بتتحمّل دفعة واحدة بدل ما تتكرر لكل زوج.
        /// مستخدمة في حفظة رحلة الإنتاج (ProductionFlowService) اللي بتعالج
        /// كذا نصيب في نفس المعاملة — كانت كل نصيب بينده استعلامين+ لوحده.
        /// </summary>
        public async Task TryGrowAutoAddedSkillsAsync(IReadOnlyList<(int WorkerId, int ProductionStageId)> pairs, DateTime asOf)
        {
            if (pairs.Count == 0) return;

            var workerIds = pairs.Select(p => p.WorkerId).Distinct().ToList();
            var skillsByKey = (await _skills.FindAsync(s => workerIds.Contains(s.WorkerId)))
                .ToDictionary(s => (s.WorkerId, s.ProductionStageId));

            var from = asOf.Date.AddDays(-LookbackDays);
            var recordsByWorker = new Dictionary<int, List<DailyProduction>>();
            foreach (var workerId in workerIds)
                recordsByWorker[workerId] = (await _production.GetByWorkerAndRangeAsync(workerId, from, asOf.Date)).ToList();

            var dirty = false;
            var grown = new List<(WorkerSkill Skill, int OldStars, int NewStars, int TimesSelected)>();

            foreach (var (workerId, stageId) in pairs.Distinct())
            {
                if (!skillsByKey.TryGetValue((workerId, stageId), out var skill) || !skill.IsAutoAdded) continue;

                var stageRecords = recordsByWorker[workerId].Where(r => r.ProductionStageId == stageId).ToList();
                var sample = MeasureFromRecords(stageRecords);
                if (sample is not null)
                {
                    skill.MeasuredRatio = sample.Value.Ratio;
                    skill.MeasuredDays = sample.Value.Days;
                    skill.MeasuredAt = DateTime.Now;
                    _skills.Update(skill);
                    dirty = true;
                }

                var timesSelected = await _production.CountByWorkerAndStageAsync(workerId, stageId);
                var measuredRatio = skill.MeasuredDays >= MinSampleDays ? (decimal?)skill.MeasuredRatio : null;
                var newStars = SkillGrowthCalculator.ComputeNextStars(skill.Stars, timesSelected, measuredRatio);
                if (newStars <= skill.Stars) continue;

                var oldStars = skill.Stars;
                skill.Stars = newStars;
                _skills.Update(skill);
                dirty = true;
                grown.Add((skill, oldStars, newStars, timesSelected));
            }

            if (dirty) await _skills.SaveChangesAsync();

            foreach (var g in grown)
            {
                var worker = await _workers.GetByIdAsync(g.Skill.WorkerId);
                await _log.LogAsync(
                    ActivityEventType.SkillAutoAdjusted, nameof(WorkerSkill), g.Skill.Id,
                    entityName: worker?.FullName,
                    details: $"{g.OldStars} → {g.NewStars}★ بعد {g.TimesSelected} اختيار، متوسط إنتاج {g.Skill.MeasuredRatio:0.00}");
            }
        }

        // ======================= المراجعة الشهرية =======================

        /// <summary>
        /// بيجهّز مراجعة التقييمات: مين اكتسب مهارات جديدة الفترة دي
        /// ويستاهل رفع نجومه على مهاراته القديمة.
        ///
        /// بيقيس الأول (عشان MeasuredRatio/MeasuredDays يبقوا طازة
        /// ويتعرضوا كمعلومة جنب الاقتراح)، وبعدين بيحسب لكل عامل عدد
        /// المهارات اللي اتضافتله في آخر <see cref="LookbackDays"/> يوم.
        /// عامل اكتسب مهارة جديدة أو أكتر = اقتراح رفع نجمة (سقف 5) على
        /// **كل مهاراته القديمة** اللي دخلت المراجعة — إشارة تطوّر
        /// حقيقية بديلة عن عدد الضربات اللي بيضربها على مهارة عارفها
        /// بالفعل. المهارة الجديدة نفسها مستثناة من الاقتراح ده: تقييمها
        /// الأول بييجي زي أي مهارة جديدة عادي مش برفعها فوق نفسها.
        ///
        /// المهارات اللي الاقتراح فيها **يساوي** النجوم الحالية **والمدير
        /// قال رأيه فيها قبل كده** مش بتظهر — مفيش داعي يبص على اللي
        /// مظبوط أصلاً. الاستثناء مهم: مهارة عمرها ما اتقيّمت بإيد
        /// (النجوم عليها حطها الترحيل مش المدير) بتتعرض عليه مرة واحدة
        /// يأكّدها حتى لو الاقتراح بيساوي الحالي، وبعدها بتسكت للأبد —
        /// من غيره مصنع لسه بادئ عمره ما يعرف الميزة موجودة أصلاً.
        /// </summary>
        public async Task<SkillReviewDto> BuildReviewAsync(DateTime asOf)
        {
            await MeasureAllAsync(asOf);

            var cutoff = asOf.Date.AddDays(-LookbackDays);

            // عدد المهارات الجديدة لكل عامل في نفس الفترة — الإشارة اللي
            // بتحرّك الاقتراح دلوقتي. بنقرأها من غير فلتر "اتقاست" لأن
            // مهارة جديدة غالبًا لسه مالهاش إنتاج خالص وقت المراجعة.
            var newSkillsByWorker = (await _skills.FindAsync(s => s.CreatedAt >= cutoff))
                .GroupBy(s => s.WorkerId)
                .ToDictionary(g => g.Key, g => g.Count());

            var all = await _skills.GetAllMeasuredAsync();
            var suggestions = new List<SkillSuggestionDto>();

            foreach (var skill in all)
            {
                // مفيش قياس كفاية = مفيش رأي. اقتراح مبني على يومين
                // أسوأ من مفيش اقتراح
                if (skill.MeasuredAt is null || skill.MeasuredDays < MinSampleDays) continue;

                // مهارة اتضافت هي نفسها في الفترة دي — تقييمها الأول
                // بييجي زي أي مهارة جديدة عادي، مش برفعها فوق نفسها
                if (skill.CreatedAt >= cutoff) continue;

                var newSkillsCount = newSkillsByWorker.GetValueOrDefault(skill.WorkerId);
                var suggested = newSkillsCount > 0 ? Math.Min(5, skill.Stars + 1) : skill.Stars;

                // مظبوط + المدير أكّده قبل كده = مفيش حاجة يتسأل عنها تاني
                if (suggested == skill.Stars && skill.StarsUpdatedAt is not null) continue;

                suggestions.Add(new SkillSuggestionDto
                {
                    WorkerId = skill.WorkerId,
                    WorkerName = skill.Worker.FullName,
                    StageId = skill.ProductionStageId,
                    StageName = skill.ProductionStage.StageName,
                    ProductName = skill.ProductionStage.Product?.Name ?? "",
                    CurrentStars = skill.Stars,
                    SuggestedStars = suggested,
                    MeasuredRatio = skill.MeasuredRatio,
                    MeasuredDays = skill.MeasuredDays,
                    NewSkillsCount = newSkillsCount,
                    StarsUpdatedAt = skill.StarsUpdatedAt
                });
            }

            return new SkillReviewDto
            {
                GeneratedAt = asOf,
                Suggestions = suggestions
                    // الأكبر فرقًا الأول — دول اللي محتاجين نظرة فعلاً
                    .OrderByDescending(s => Math.Abs(s.SuggestedStars - s.CurrentStars))
                    .ThenByDescending(s => s.MeasuredDays)
                    .ToList()
            };
        }

        /// <summary>
        /// يطبّق اقتراح: بيحطّ النجوم المقترحة كأن المدير حطّها بإيده.
        /// بيتنادى لما يدوس "وافق" في شاشة المراجعة.
        /// </summary>
        public Task ApplySuggestionAsync(SkillSuggestionDto suggestion) =>
            SetStarsAsync(suggestion.WorkerId, suggestion.StageId, suggestion.SuggestedStars);

        // ======================= الاستهلاك في الشاشات =======================

        /// <summary>
        /// ترتيب المؤهلين لمرحلة: الأحسن الأول.
        ///
        /// **القاعدة دي المكان الوحيد اللي بيقرر مين يبان الأول.** دالة
        /// نقية عن قصد عشان الشاشتين اللي بتستخدماها يقدروا ينادوها
        /// بطريقتين مختلفتين من غير ما القاعدة تتكرر:
        ///   • شاشة المنتجات بتسأل عن مرحلة واحدة (<see cref="GetRankedForStageAsync"/>)
        ///   • شاشة التسجيل اليومي بتحمّل مهارات المنتج كله في استعلام
        ///     واحد وبترتّب كل مرحلة من اللي في الذاكرة — استعلام لكل
        ///     مرحلة كان هيبقى 11 استعلام لمنتج من 11 مرحلة
        ///
        /// النجوم الأول (رأي المدير)، وبعدين الأداء المقاس بيفصل بين
        /// المتساويين، وبعدين ترتيب العامل المخصص عشان الترتيب يبقى ثابت.
        /// </summary>
        public static IEnumerable<WorkerSkill> Rank(IEnumerable<WorkerSkill> skills) =>
            skills
                .OrderByDescending(s => s.Stars)
                .ThenByDescending(s => s.MeasuredRatio)
                .ThenBy(s => s.Worker.SortOrder);

        /// <summary>
        /// العمال المؤهلين لمرحلة، مرتبين بالنجوم من الأحسن للأضعف.
        ///
        /// ده اللي بتستخدمه شاشة المنتجات لما المستخدم يسأل "مين يعرف
        /// المرحلة دي؟".
        /// </summary>
        public async Task<IReadOnlyList<RankedWorkerDto>> GetRankedForStageAsync(int stageId) =>
            Rank(await _skills.GetByStageAsync(stageId)).Select(ToRanked).ToList();

        /// <summary>مهارات عامل واحد بنجومها (لبروفايله)</summary>
        public async Task<IReadOnlyList<RankedWorkerDto>> GetForWorkerAsync(int workerId)
        {
            var skills = await _skills.GetByWorkerAsync(workerId);
            return skills.Select(ToRanked).ToList();
        }

        private static RankedWorkerDto ToRanked(WorkerSkill s) => new()
        {
            WorkerId = s.WorkerId,
            WorkerName = s.Worker?.FullName ?? "",
            StageId = s.ProductionStageId,
            StageName = s.ProductionStage?.StageName ?? "",
            Stars = s.Stars,
            MeasuredRatio = s.MeasuredRatio,
            MeasuredDays = s.MeasuredDays,
            MeasuredAt = s.MeasuredAt,
            StarsUpdatedAt = s.StarsUpdatedAt
        };
    }
}
