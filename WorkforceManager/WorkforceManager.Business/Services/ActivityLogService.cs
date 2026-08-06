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

        // ------- التنظيف التلقائي -------

        /// <summary>أقل مدة احتفاظ مسموح بيها — أقل من كده السجل بيبقى بلا معنى</summary>
        public const int MinRetentionDays = 30;

        /// <summary>
        /// يمسح الأحداث اللي عدّت مدة الاحتفاظ. بيتنادى مرة عند تشغيل
        /// البرنامج، بعد ما النسخة الاحتياطية تكون اتاخدت — فأي حاجة
        /// اتمسحت هنا لسه موجودة في نسخة اليوم.
        ///
        /// مدتين مش واحدة: أحداث الفلوس بتعيش أطول لأن السؤال عليها
        /// بييجي متأخر ("ليه خصمت مني في أغسطس؟")، وأحداث الحذف الإداري
        /// بتفقد قيمتها بسرعة. التقسيم في
        /// <see cref="Core.Enums.ActivityEventRetention"/>.
        ///
        /// صفر في أي مدة = التنظيف متوقف للنوع ده (اختيار المستخدم من
        /// الإعدادات)، مش "امسح كل حاجة".
        /// </summary>
        /// <returns>عدد الأحداث اللي اتمسحت</returns>
        public async Task<int> PurgeExpiredAsync(int shortLivedDays, int longLivedDays)
        {
            var today = DateTime.Today;
            var deleted = 0;

            if (shortLivedDays > 0)
                deleted += await _events.DeleteOlderThanAsync(
                    today.AddDays(-Math.Max(shortLivedDays, MinRetentionDays)),
                    ActivityEventRetention.ShortLivedTypes);

            if (longLivedDays > 0)
                deleted += await _events.DeleteOlderThanAsync(
                    today.AddDays(-Math.Max(longLivedDays, MinRetentionDays)),
                    ActivityEventRetention.LongLivedTypes);

            return deleted;
        }
    }
}
