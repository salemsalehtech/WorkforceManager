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
        private readonly CurrentUserContext _currentUser;

        public SkillRatingService(
            IWorkerSkillRepository skills,
            IDailyProductionRepository production,
            CurrentUserContext currentUser)
        {
            _skills = skills;
            _production = production;
            _currentUser = currentUser;
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
        /// </summary>
        /// <returns>عدد المهارات اللي اتقاس أداؤها</returns>
        public async Task<int> MeasureAllAsync(DateTime asOf)
        {
            var from = asOf.Date.AddDays(-LookbackDays);
            var records = await _production.GetByRangeAsync(from, asOf.Date);

            var byWorkerStage = records
                .GroupBy(r => (r.WorkerId, r.ProductionStageId))
                .ToDictionary(g => g.Key, g => g.ToList());

            var measured = 0;
            foreach (var ((workerId, stageId), stageRecords) in byWorkerStage)
            {
                var skill = await _skills.GetAsync(workerId, stageId);
                if (skill is null) continue;

                var sample = MeasureFromRecords(stageRecords);
                if (sample is null) continue;

                skill.MeasuredRatio = sample.Value.Ratio;
                skill.MeasuredDays = sample.Value.Days;
                skill.MeasuredAt = DateTime.Now;

                _skills.Update(skill);
                measured++;
            }

            if (measured > 0) await _skills.SaveChangesAsync();
            return measured;
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
        /// المتساويين، وبعدين الاسم عشان الترتيب يبقى ثابت.
        /// </summary>
        public static IEnumerable<WorkerSkill> Rank(IEnumerable<WorkerSkill> skills) =>
            skills
                .OrderByDescending(s => s.Stars)
                .ThenByDescending(s => s.MeasuredRatio)
                .ThenBy(s => s.Worker.FullName);

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
