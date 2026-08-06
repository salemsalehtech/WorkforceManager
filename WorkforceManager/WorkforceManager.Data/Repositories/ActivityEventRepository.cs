using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class ActivityEventRepository : GenericRepository<ActivityEvent>, IActivityEventRepository
    {
        public ActivityEventRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int take)
        {
            return await DbSet
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ActivityEvent>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Where(e => e.OccurredAt.Date >= from.Date && e.OccurredAt.Date <= to.Date)
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .ToListAsync();
        }

        /// <summary>
        /// حذف مجمّع بـ ExecuteDelete: بيتنفّذ كأمر DELETE واحد على
        /// الداتابيز من غير ما يحمّل الصفوف في الذاكرة — سجل سنة كامل
        /// ممكن يكون آلاف الصفوف ومفيش داعي يعدّوا على التطبيق عشان
        /// يتمسحوا. العمود OccurredAt عليه فهرس أصلاً.
        /// </summary>
        public async Task<int> DeleteOlderThanAsync(
            DateTime cutoff, IReadOnlyCollection<Core.Enums.ActivityEventType> types)
        {
            if (types.Count == 0) return 0;

            return await DbSet
                .Where(e => e.OccurredAt < cutoff && types.Contains(e.EventType))
                .ExecuteDeleteAsync();
        }
    }
}
