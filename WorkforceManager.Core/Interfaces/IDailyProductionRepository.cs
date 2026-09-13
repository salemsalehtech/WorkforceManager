using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IDailyProductionRepository : IGenericRepository<DailyProduction>
    {
        /// <summary>كل سجلات الإنتاج في تاريخ معين (لحساب أجور اليوم وتقييم كل العمال)</summary>
        Task<IReadOnlyList<DailyProduction>> GetByDateAsync(DateTime date);

        /// <summary>كل سجلات عامل معين خلال فترة زمنية (لعرض تاريخه وأداءه)</summary>
        Task<IReadOnlyList<DailyProduction>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to);

        /// <summary>كل سجلات الإنتاج لكل العمال خلال فترة زمنية (للملخص والتقرير الأسبوعي المجمّع)</summary>
        Task<IReadOnlyList<DailyProduction>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>
        /// مجموع القطع لكل مرحلة في يوم واحد — إنتاج اليوم على كل مرحلة.
        /// المفتاح = ProductionStageId.
        /// </summary>
        Task<IReadOnlyDictionary<int, int>> GetStageTotalsOnAsync(DateTime date);

        /// <summary>
        /// مجموع القطع لكل مرحلة من أول التسجيل لحد اليوم ده (شامله).
        ///
        /// ده أساس حساب **الشغل الواقف**: القطع الواقفة قبل مرحلة معينة =
        /// إجمالي اللي خلص المرحلة اللي قبلها ناقص إجمالي اللي خلصها هي.
        /// لازم يكون تراكمي من أول التسجيل مش لليوم بس — القطعة اللي عدّت
        /// مرحلة إمبارح ولسه واقفة النهارده لازم تبان.
        ///
        /// التجميع بيتم في الداتابيز مش في الذاكرة.
        /// </summary>
        Task<IReadOnlyDictionary<int, int>> GetStageTotalsUpToAsync(DateTime date);

        /// <summary>
        /// فيه أي سجل إنتاج للعامل ده؟ **بيتجاهل فلتر الحذف الناعم**:
        /// السجل المتشال ناعم لسه ماسك المفتاح الأجنبي، فلسه بيمنع مسح
        /// العامل نهائيًا حتى لو مش ظاهر في أي شاشة.
        /// </summary>
        Task<bool> HasAnyForWorkerAsync(int workerId);

        /// <summary>نفس الفكرة بالظبط بس على المرحلة</summary>
        Task<bool> HasAnyForStageAsync(int stageId);
    }
}
