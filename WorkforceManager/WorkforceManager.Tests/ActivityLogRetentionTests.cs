using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// تنظيف سجل العمليات التلقائي.
    ///
    /// السجل ده مالوش صف "روتيني" — كل حدث فيه إما حذف أو حركة فلوس.
    /// عشان كده المدة مدتين: الحذف الإداري بيفقد قيمته بسرعة، وأحداث
    /// الفلوس السؤال عليها بييجي بعد شهور ("ليه خصمت مني في أغسطس؟").
    /// </summary>
    public class ActivityLogRetentionTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task AddEventAsync(ActivityEventType type, int daysAgo)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.ActivityEvents.Add(new ActivityEvent
            {
                EventType = type,
                EntityType = "Test",
                EntityId = 1,
                Actor = "مدير",
                OccurredAt = DateTime.Today.AddDays(-daysAgo)
            });
            await db.SaveChangesAsync();
        }

        private async Task<int> PurgeAsync(int shortLivedDays = 90, int longLivedDays = 365)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<ActivityLogService>(scope)
                .PurgeExpiredAsync(shortLivedDays, longLivedDays);
        }

        private async Task<List<ActivityEventType>> RemainingAsync()
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<AppDbContext>(scope)
                .ActivityEvents.Select(e => e.EventType).ToListAsync();
        }

        // ======================= المدة القصيرة =======================

        [Fact]
        public async Task An_old_deletion_event_is_purged()
        {
            await AddEventAsync(ActivityEventType.ProductionDayDeleted, daysAgo: 120);

            Assert.Equal(1, await PurgeAsync());
            Assert.Empty(await RemainingAsync());
        }

        [Fact]
        public async Task A_deletion_event_inside_the_window_stays()
        {
            await AddEventAsync(ActivityEventType.ProductionDayDeleted, daysAgo: 60);

            Assert.Equal(0, await PurgeAsync());
            Assert.Single(await RemainingAsync());
        }

        // ======================= المدة الطويلة =======================

        [Fact]
        public async Task A_money_event_survives_the_short_window()
        {
            // ٤ شهور: الحذف الإداري كان هيروح، بس ده تغيير أجر —
            // ده بالظبط اللي بيتسأل عنه متأخر
            await AddEventAsync(ActivityEventType.WorkerWageChanged, daysAgo: 120);

            Assert.Equal(0, await PurgeAsync());
            Assert.Single(await RemainingAsync());
        }

        [Fact]
        public async Task A_money_event_older_than_a_year_is_purged()
        {
            await AddEventAsync(ActivityEventType.WageAdjustmentSaved, daysAgo: 400);

            Assert.Equal(1, await PurgeAsync());
            Assert.Empty(await RemainingAsync());
        }

        [Fact]
        public async Task The_two_windows_apply_to_the_right_events_in_one_pass()
        {
            await AddEventAsync(ActivityEventType.ProductionRecordDeleted, daysAgo: 120); // يروح
            await AddEventAsync(ActivityEventType.PenaltySaved, daysAgo: 120);            // يفضل
            await AddEventAsync(ActivityEventType.WorkerDeleted, daysAgo: 10);            // يفضل
            await AddEventAsync(ActivityEventType.PenaltyDeleted, daysAgo: 400);          // يروح

            Assert.Equal(2, await PurgeAsync());

            var left = await RemainingAsync();
            Assert.Contains(ActivityEventType.PenaltySaved, left);
            Assert.Contains(ActivityEventType.WorkerDeleted, left);
            Assert.Equal(2, left.Count);
        }

        // ======================= الإيقاف =======================

        [Fact]
        public async Task Zero_means_off_not_delete_everything()
        {
            await AddEventAsync(ActivityEventType.ProductionDayDeleted, daysAgo: 900);
            await AddEventAsync(ActivityEventType.WorkerWageChanged, daysAgo: 900);

            Assert.Equal(0, await PurgeAsync(shortLivedDays: 0, longLivedDays: 0));
            Assert.Equal(2, (await RemainingAsync()).Count);
        }

        [Fact]
        public async Task Turning_one_window_off_leaves_the_other_working()
        {
            await AddEventAsync(ActivityEventType.ProductionDayDeleted, daysAgo: 900);
            await AddEventAsync(ActivityEventType.WorkerWageChanged, daysAgo: 900);

            Assert.Equal(1, await PurgeAsync(shortLivedDays: 90, longLivedDays: 0));
            Assert.Equal(ActivityEventType.WorkerWageChanged, Assert.Single(await RemainingAsync()));
        }

        [Fact]
        public async Task A_window_below_the_minimum_is_raised_to_it()
        {
            // سجل بيتمسح كل أسبوع مش سجل. الحد الأدنى بيحمي من رقم
            // اتكتب غلط في الإعدادات.
            await AddEventAsync(ActivityEventType.ProductionDayDeleted,
                daysAgo: ActivityLogService.MinRetentionDays - 5);

            Assert.Equal(0, await PurgeAsync(shortLivedDays: 1, longLivedDays: 1));
            Assert.Single(await RemainingAsync());
        }

        // ======================= الافتراضي الآمن =======================

        [Fact]
        public void Every_event_type_that_is_not_listed_as_short_lived_gets_the_long_window()
        {
            // القاعدة معكوسة عن قصد: أي نوع حدث جديد يتضاف بعدين بياخد
            // المدة الطويلة تلقائيًا. الاختبار ده بيقع لو حد قلبها.
            var all = Enum.GetValues<ActivityEventType>();
            var shortLived = ActivityEventRetention.ShortLivedTypes;
            var longLived = ActivityEventRetention.LongLivedTypes;

            Assert.Equal(all.Length, shortLived.Count + longLived.Count);
            Assert.Empty(longLived.Intersect(shortLived));

            // كل حدث فلوس أو أمان لازم يكون في الطويلة
            Assert.Contains(ActivityEventType.WorkerWageChanged, longLived);
            Assert.Contains(ActivityEventType.ProductionPiecesEdited, longLived);
            Assert.Contains(ActivityEventType.PenaltySaved, longLived);
            Assert.Contains(ActivityEventType.PenaltyDeleted, longLived);
            Assert.Contains(ActivityEventType.WageAdjustmentSaved, longLived);
            Assert.Contains(ActivityEventType.OperationsPasswordChanged, longLived);
        }
    }
}
