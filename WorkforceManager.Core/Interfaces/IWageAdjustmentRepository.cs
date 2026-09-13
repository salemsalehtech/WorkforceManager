using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IWageAdjustmentRepository : IGenericRepository<WageAdjustment>
    {
        /// <summary>كل تعديلات أجر عامل معين خلال فترة (لعرضها في تقريره وقسيمة أجره)</summary>
        Task<IReadOnlyList<WageAdjustment>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to);

        /// <summary>كل تعديلات الأجر خلال فترة لكل العمال (لكشف أجور الفترة)</summary>
        Task<IReadOnlyList<WageAdjustment>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>تعديلات يوم معين لكل العمال (لعرضها وحذفها في شاشة التسجيل اليومي)</summary>
        Task<IReadOnlyList<WageAdjustment>> GetByDateAsync(DateTime date);

        /// <summary>فيه أي سلفة أو حافز للعامل ده؟</summary>
        Task<bool> HasAnyForWorkerAsync(int workerId);
    }
}
