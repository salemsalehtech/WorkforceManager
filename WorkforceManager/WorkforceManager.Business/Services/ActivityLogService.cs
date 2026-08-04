using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// سجل العمليات — المكان الوحيد اللي بيكتب حدث في البرنامج.
    ///
    /// أي خدمة عايزة تسجّل "حصل كذا" بتنادي <see cref="LogAsync"/>.
    /// ممنوع أي حد يعمل Add على جدول الأحداث مباشرة، عشان اسم الفاعل
    /// والوقت ياخدوا نفس المعاملة في كل حدث.
    /// </summary>
    public class ActivityLogService
    {
        private readonly IActivityEventRepository _events;
        private readonly CurrentUserContext _currentUser;

        public ActivityLogService(IActivityEventRepository events, CurrentUserContext currentUser)
        {
            _events = events;
            _currentUser = currentUser;
        }

        /// <summary>
        /// يسجّل حدث.
        /// </summary>
        /// <param name="saveChanges">
        /// false لما الاستدعاء جوه معاملة أكبر (زي الحذف الناعم) — الحفظ
        /// ساعتها بيتم مرة واحدة مع باقي التغييرات، فالحدث والحذف بيتحفظوا
        /// كوحدة واحدة: يا الاتنين يا ولا واحد.
        /// </param>
        public async Task<ActivityEvent> LogAsync(
            ActivityEventType eventType,
            string entityType,
            int entityId,
            string? entityName = null,
            string? reason = null,
            string? details = null,
            bool saveChanges = true)
        {
            var activityEvent = new ActivityEvent
            {
                EventType = eventType,
                EntityType = entityType,
                EntityId = entityId,
                EntityName = entityName,
                Actor = _currentUser.ActorName,
                OccurredAt = DateTime.Now,
                Reason = reason,
                Details = details
            };

            await _events.AddAsync(activityEvent);
            if (saveChanges) await _events.SaveChangesAsync();

            return activityEvent;
        }

        /// <summary>أحدث الأحداث للعرض في شاشة السجل</summary>
        public Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int take = 200) =>
            _events.GetRecentAsync(take);

        /// <summary>أحداث فترة معينة (فلترة بالتاريخ في شاشة السجل)</summary>
        public Task<IReadOnlyList<ActivityEvent>> GetByRangeAsync(DateTime from, DateTime to) =>
            _events.GetByRangeAsync(from, to);
    }
}
