using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IPenaltyRepository : IGenericRepository<Penalty>
    {
        /// <summary>كل جزاءات عامل معين خلال فترة زمنية (لحساب خصومات الأسبوع وعرضها في تقريره)</summary>
        Task<IReadOnlyList<Penalty>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to);

        /// <summary>كل الجزاءات المسجّلة خلال فترة زمنية لكل العمال (للتقرير الأسبوعي المجمّع)</summary>
        Task<IReadOnlyList<Penalty>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>فيه أي جزاء على العامل ده؟</summary>
        Task<bool> HasAnyForWorkerAsync(int workerId);
    }
}
