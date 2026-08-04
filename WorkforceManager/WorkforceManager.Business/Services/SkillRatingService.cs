using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تقييم مهارة العامل على المرحلة — القاعدة الوحيدة للحساب والترتيب.
    ///
    /// القاعدة المتفق عليها:
    ///   التقييم = متوسط (إنتاج العامل في اليوم ÷ الكوتة المعيارية للمرحلة)
    ///             على مدار آخر <see cref="LookbackDays"/> يوم فيهم شغل.
    ///
    /// ليه نسبة مش عدد قطع: كوتات المراحل مختلفة تمامًا (5000 قطعة في
    /// مرحلة و80 في مرحلة تانية)، فمقارنة الأعداد الخام بين مرحلتين
    /// مالهاش أي معنى. النسبة بتخلي كل المراحل على نفس المسطرة.
    ///
    /// التقييم اليدوي بيفضل هو المعروض لحد ما يبقى فيه إنتاج كفاية
    /// (<see cref="MinSampleDays"/> أيام): تقييم مبني على يوم واحد
    /// بيتقلب مع أي يوم شاذ، والمستخدم بيفقد الثقة في الرقم.
    /// </summary>
    public class SkillRatingService
    {
        /// <summary>الفترة اللي الحساب التلقائي بيبص عليها</summary>
        public const int LookbackDays = 30;

        /// <summary>أقل عدد أيام شغل قبل ما النظام يستبدل التقييم اليدوي</summary>
        public const int MinSampleDays = 3;

        /// <summary>حد "خبير": بيعمل الكوتة وزيادة 15%</summary>
        public const decimal ExpertThreshold = 1.15m;

        /// <summary>حد "متمكن": بيعمل 85% من الكوتة على الأقل</summary>
        public const decimal ProficientThreshold = 0.85m;

        private readonly IWorkerSkillRepository _skills;
        private readonly IDailyProductionRepository _production;

        public SkillRatingService(
            IWorkerSkillRepository skills,
            IDailyProductionRepository production)
        {
            _skills = skills;
            _production = production;
        }

        // ======================= القاعدة النقية =======================

        /// <summary>
        /// يحوّل نسبة الأداء لمستوى معروض. دالة نقية عشان الواجهة
        /// والتقارير يستخدموها من غير قاعدة بيانات.
        /// </summary>
        public static SkillLevel LevelFor(decimal ratingValue) => ratingValue switch
        {
            >= ExpertThreshold => SkillLevel.Expert,
            >= ProficientThreshold => SkillLevel.Proficient,
            _ => SkillLevel.Beginner
        };

        /// <summary>
        /// متوسط تقييم العامل على منتج = متوسط المراحل **اللي هو مربوط
        /// بيها فعلاً** في المنتج ده.
        ///
        /// المراحل اللي مالوش فيها مهارة مش بتتحسب صفر عن قصد: العامل
        /// المتخصص في 3 مراحل من 11 مش ضعيف، هو متخصص — وحسابه صفر في
        /// الباقي كان هيخلي كل المتخصصين يبانوا سيئين.
        ///
        /// بيرجّع null لو مالوش أي مهارة في المنتج ده.
        /// </summary>
        public static decimal? ProductRating(IEnumerable<WorkerSkill> skillsOnProduct)
        {
            var values = skillsOnProduct.Select(s => s.RatingValue).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 2);
        }

        // ======================= التقييم اليدوي =======================

        /// <summary>
        /// يحطّ تقييم يدوي لعامل على مرحلة (بيتنادى وقت ربط المهارة).
        /// بيسجّل القيمة كـ LastManualValue كمان عشان تفضل معروفة حتى
        /// بعد ما النظام يحسب فوقها.
        /// </summary>
        public async Task SetManualRatingAsync(int workerId, int stageId, decimal ratingValue)
        {
            if (ratingValue <= 0)
                throw new InvalidOperationException("التقييم لازم يكون رقم موجب");

            var skill = await _skills.GetAsync(workerId, stageId)
                ?? throw new InvalidOperationException("العامل ده مش مربوط بالمرحلة دي");

            skill.RatingValue = ratingValue;
            skill.LastManualValue = ratingValue;
            skill.RatingSource = SkillRatingSource.Manual;
            skill.Level = LevelFor(ratingValue);

            _skills.Update(skill);
            await _skills.SaveChangesAsync();
        }

        // ======================= التقييم التلقائي =======================

        /// <summary>
        /// يعيد حساب تقييمات عامل من إنتاجه الفعلي.
        ///
        /// بيعدّي على المراحل اللي ليها إنتاج كفاية بس — الباقي بيفضل
        /// على تقييمه اليدوي، والواجهة بتوضّح الفرق.
        /// </summary>
        /// <returns>عدد المراحل اللي اتحدّثت</returns>
        public async Task<int> RecalculateForWorkerAsync(int workerId, DateTime asOf)
        {
            var from = asOf.Date.AddDays(-LookbackDays);
            var records = (await _production.GetByWorkerAndRangeAsync(workerId, from, asOf.Date)).ToList();
            if (records.Count == 0) return 0;

            var skills = await _skills.GetByWorkerAsync(workerId);
            var updated = 0;

            foreach (var skill in skills)
            {
                var sample = ComputeFromRecords(
                    records.Where(r => r.ProductionStageId == skill.ProductionStageId));

                if (sample is null) continue;

                skill.RatingValue = sample.Value.Rating;
                skill.RatingSource = SkillRatingSource.Auto;
                skill.AutoSampleDays = sample.Value.Days;
                skill.LastAutoCalculatedAt = DateTime.Now;
                skill.Level = LevelFor(sample.Value.Rating);

                _skills.Update(skill);
                updated++;
            }

            if (updated > 0) await _skills.SaveChangesAsync();
            return updated;
        }

        /// <summary>
        /// نسبة الأداء من سجلات مرحلة واحدة، ومعاها عدد الأيام اللي
        /// اتبنت عليها. دالة نقية — دي اللي بتخلي الحساب قابل للشرح
        /// والاختبار من غير قاعدة بيانات.
        ///
        /// بترجّع null لو الأيام أقل من الحد الأدنى.
        /// </summary>
        public static (decimal Rating, int Days)? ComputeFromRecords(IEnumerable<DailyProduction> stageRecords)
        {
            // اليوم الواحد ممكن يكون فيه أكتر من سجل على نفس المرحلة،
            // والتقييم بيتحسب على اليوم مش على السجل: عامل اتسجل له
            // سجلين نص كوتة كل واحد عمل كوتة كاملة، مش نص كوتة مرتين
            var perDay = stageRecords
                .Where(r => r.PiecesPerWorkdayAtEntry > 0)
                .GroupBy(r => r.Date.Date)
                .Select(g => (decimal)g.Sum(r => r.PieceCount) / g.First().PiecesPerWorkdayAtEntry)
                .ToList();

            if (perDay.Count < MinSampleDays) return null;

            return (Math.Round(perDay.Average(), 2), perDay.Count);
        }

        // ======================= الاستهلاك في الشاشات =======================

        /// <summary>
        /// العمال المؤهلين لمرحلة، مرتبين من الأحسن للأضعف.
        ///
        /// ده اللي بتستخدمه شاشة التسجيل اليومي لما المستخدم يدوّر على
        /// عامل يضيفه، وشاشة المنتجات لما يختار مرحلة.
        /// </summary>
        public async Task<IReadOnlyList<RankedWorkerDto>> GetRankedForStageAsync(int stageId)
        {
            var skills = await _skills.GetByStageAsync(stageId);

            return skills
                .OrderByDescending(s => s.RatingValue)
                .ThenBy(s => s.Worker.FullName)
                .Select(s => new RankedWorkerDto
                {
                    WorkerId = s.WorkerId,
                    WorkerName = s.Worker.FullName,
                    RatingValue = s.RatingValue,
                    Level = s.Level,
                    Source = s.RatingSource,
                    SampleDays = s.AutoSampleDays,
                    LastManualValue = s.LastManualValue
                })
                .ToList();
        }
    }
}
