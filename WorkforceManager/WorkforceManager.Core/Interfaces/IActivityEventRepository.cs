using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IActivityEventRepository : IGenericRepository<ActivityEvent>
    {
        /// <summary>أحدث الأحداث، الأجدد الأول (لشاشة سجل العمليات)</summary>
        Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int take);

        /// <summary>أحداث فترة معينة، الأجدد الأول</summary>
        Task<IReadOnlyList<ActivityEvent>> GetByRangeAsync(DateTime from, DateTime to);
    }
}
