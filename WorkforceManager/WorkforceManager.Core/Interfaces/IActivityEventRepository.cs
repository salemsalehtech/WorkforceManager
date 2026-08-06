using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IActivityEventRepository : IGenericRepository<ActivityEvent>
    {
        /// <summary>أحدث الأحداث، الأجدد الأول (لشاشة سجل العمليات)</summary>
        Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int take);

        /// <summary>أحداث فترة معينة، الأجدد الأول</summary>
        Task<IReadOnlyList<ActivityEvent>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>
        /// يمسح الأحداث من الأنواع دي اللي أقدم من <paramref name="cutoff"/>،
        /// ويرجّع عددها. حذف نهائي (مفيش حذف ناعم لسجل).
        /// </summary>
        Task<int> DeleteOlderThanAsync(DateTime cutoff, IReadOnlyCollection<Enums.ActivityEventType> types);
    }
}
