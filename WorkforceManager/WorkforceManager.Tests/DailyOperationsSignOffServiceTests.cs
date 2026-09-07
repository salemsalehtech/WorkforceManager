using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// توقيع نهاية اليوم — علم عالمي لكل تاريخ، وبديل الباسورد الفوري
    /// للشغل اليومي المتكرر (Tier B). أهم حالتين هنا: حساب الأيام الفايتة
    /// اللي محدش وقّعها (بذرة الـ cutover + الحد الفاصل عندها بالظبط)،
    /// ولحاق باسورد واحد يغطي عدة أيام.
    /// </summary>
    public class DailyOperationsSignOffServiceTests : IDisposable
    {
        private const string Password = "1234";

        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        // TestDatabase.Today ثابت (2026/7/29) — "اليوم" هنا نظير له، مش
        // DateTime.Today الحقيقي، عشان الاختبار يفضل ثابت النتيجة مهما
        // اتشغّل إمتى
        private static DateTime Today => TestDatabase.Today;
        private static DateTime Yesterday => Today.AddDays(-1);
        private static DateTime TwoDaysAgo => Today.AddDays(-2);
        private static DateTime ThreeDaysAgo => Today.AddDays(-3);

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        /// <summary>
        /// بيزرع حدث نشاط بتاريخ معيّن مباشرة — LogAsync العادي بيسجّل
        /// بالساعة الحالية دايمًا (زي أي أثر حقيقي)، والاختبار هنا محتاج
        /// نشاط في أيام فاتت فعلاً.
        /// </summary>
        private async Task SeedActivityAsync(DateTime occurredAt)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            db.ActivityEvents.Add(new ActivityEvent
            {
                EventType = ActivityEventType.ProductionRecorded,
                EntityType = "DailyProduction",
                EntityId = 1,
                Actor = "test",
                OccurredAt = occurredAt
            });
            await db.SaveChangesAsync();
        }

        private Task<List<DateTime>> UnsignedDatesAsync() => _db.InScopeAsync<DailyOperationsSignOffService, List<DateTime>>(
            async s => (await s.GetUnsignedPastDatesAsync(Today)).ToList());

        /// <summary>
        /// بيوقّع يوم مبكر يدويًا عشان يبقى "آخر يوم موقّع" معروف —
        /// من غيره أول استدعاء لـ GetUnsignedPastDatesAsync كان هيعمل
        /// بذرة الـ cutover التلقائية (أمس) وتبلع أي نشاط اتزرع قبلها،
        /// وده مش اللي الاختبارات دي عايزة تتأكد منه (نشاط في **نص**
        /// الفجوة بعد ما البرنامج اشتغل بالميزة دي بالفعل)
        /// </summary>
        private async Task EstablishBaselineSignOffAsync(DateTime date)
        {
            using var scope = _db.CreateScope();
            await _db.GetService<DailyOperationsSignOffService>(scope).SignOffAsync(date, "");
        }

        // ======================= SignOffAsync =======================

        [Fact]
        public async Task SigningOff_MarksTheDayAndLogsIt()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<DailyOperationsSignOffService>(scope).SignOffAsync(Today, "");

            using var check = _db.CreateScope();
            Assert.True(await _db.GetService<DailyOperationsSignOffService>(check).IsSignedOffAsync(Today));

            var logged = Assert.Single(await _db.GetService<AppDbContext>(check).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == ActivityEventType.DaySignedOff).ToListAsync());
            Assert.Equal($"يوم {Today:yyyy/MM/dd}", logged.EntityName);
        }

        [Fact]
        public async Task SigningOffTheSameDayAgain_UpdatesTheSignatureInsteadOfRefusing()
        {
            // الشغل اللي بيحصل بعد توقيع بيرجّع اليوم "مش موقّع"، فلازم
            // ينفع يتوقّع تاني — والصف بيفضل واحد بآخر وقت توقيع
            DateTime firstSignedAt;
            using (var scope = _db.CreateScope())
                firstSignedAt = (await _db.GetService<DailyOperationsSignOffService>(scope)
                    .SignOffAsync(Today, "")).SignedOffAt;

            await Task.Delay(15); // عشان الوقت الجديد يبقى أكبر فعلاً

            using (var scope = _db.CreateScope())
                await _db.GetService<DailyOperationsSignOffService>(scope).SignOffAsync(Today, "");

            using var check = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(check);

            var row = Assert.Single(await db.DailyOperationsSignOffs.AsNoTracking().ToListAsync());
            Assert.True(row.SignedOffAt > firstSignedAt, "لازم يتحدّث لآخر وقت توقيع");

            // والتوقيع التاني اتسجّل كحدث مستقل بتفصيلته
            var logged = await db.ActivityEvents.AsNoTracking()
                .Where(e => e.EventType == ActivityEventType.DaySignedOff).ToListAsync();
            Assert.Equal(2, logged.Count);
            Assert.Contains(logged, e => e.Details != null && e.Details.Contains("توقيع تاني"));
        }

        // ======================= الشغل اللي بعد التوقيع =======================
        //
        // SignOffAsync بيحط SignedOffAt = الساعة الحقيقية، والاختبارات كلها
        // شغالة على تاريخ ثابت (TestDatabase.Today) — فبنظبّط وقت التوقيع
        // بإيدينا بعد كل توقيع عشان "قبل/بعد" يبقى لها معنى جوه نفس اليوم

        private async Task SetSignedOffAtAsync(DateTime date, DateTime signedAt)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var row = await db.DailyOperationsSignOffs.SingleAsync(s => s.Date == date.Date);
            row.SignedOffAt = signedAt;
            await db.SaveChangesAsync();
        }

        /// <summary>يوقّع اليوم ويخلي وقت توقيعه لحظة محددة جوه نفس اليوم</summary>
        private async Task SignOffAtAsync(DateTime date, DateTime signedAt)
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<DailyOperationsSignOffService>(scope).SignOffAsync(date, "");

            await SetSignedOffAtAsync(date, signedAt);
        }

        [Fact]
        public async Task ASignedDayWithNothingAfterIt_IsFullySignedOff()
        {
            await SeedActivityAsync(Today.AddHours(9));
            await SignOffAtAsync(Today, Today.AddHours(18));

            using var scope = _db.CreateScope();
            var service = _db.GetService<DailyOperationsSignOffService>(scope);

            Assert.True(await service.IsFullySignedOffAsync(Today));
            Assert.Empty(await service.GetActivitySinceLastSignOffAsync(Today));
        }

        [Fact]
        public async Task ActivityAfterTheSignature_MakesTheDayCountAsUnsignedAgain()
        {
            // ده بالظبط اللي كشفه المستخدم: وقّع 6:37، وبعدين حذف إنتاج
            // يوم — الحذف ده مالوش أي إمضاء، فاليوم لازم يرجع محتاج توقيع
            await SignOffAtAsync(Today, Today.AddHours(18));
            await SeedActivityAsync(Today.AddHours(20)); // شغل بعد التوقيع

            using var check = _db.CreateScope();
            var service = _db.GetService<DailyOperationsSignOffService>(check);

            Assert.False(await service.IsFullySignedOffAsync(Today));
            Assert.Single(await service.GetActivitySinceLastSignOffAsync(Today));
        }

        [Fact]
        public async Task TheSignOffEventItself_NeverReArmsTheGuard()
        {
            // حدث DaySignedOff بيتكتب **بعد** ما الصف يتحفظ بأجزاء من
            // الثانية (LogAsync بعد الـ commit) — من غير استثنائه كل توقيع
            // كان هيبطّل نفسه فورًا ويطلب توقيع تاني للأبد
            await SignOffAtAsync(Today, Today.AddHours(18));

            using (var scope = _db.CreateScope())
            {
                var db = _db.GetService<AppDbContext>(scope);
                db.ActivityEvents.Add(new ActivityEvent
                {
                    EventType = ActivityEventType.DaySignedOff,
                    EntityType = "DailyOperationsSignOff",
                    EntityId = 1,
                    Actor = "test",
                    OccurredAt = Today.AddHours(18).AddSeconds(1) // بعد التوقيع بلحظة
                });
                await db.SaveChangesAsync();
            }

            using var check = _db.CreateScope();
            Assert.True(await _db.GetService<DailyOperationsSignOffService>(check)
                .IsFullySignedOffAsync(Today));
        }

        [Fact]
        public async Task TheSummaryShowsOnlyWhatCameAfterTheLastSignature()
        {
            await SeedActivityAsync(Today.AddHours(9));  // قبل التوقيع — موقّع عليه خلاص
            await SignOffAtAsync(Today, Today.AddHours(18));
            await SeedActivityAsync(Today.AddHours(19)); // بعد التوقيع
            await SeedActivityAsync(Today.AddHours(20));

            using var check = _db.CreateScope();
            var pending = await _db.GetService<DailyOperationsSignOffService>(check)
                .GetActivitySinceLastSignOffAsync(Today);

            Assert.Equal(2, pending.Count); // اللي قبل التوقيع مش معروض
        }

        [Fact]
        public async Task APastDaySignedThenTouchedAgain_IsFlaggedForCatchUp()
        {
            await EstablishBaselineSignOffAsync(Today.AddDays(-4));

            // امبارح: نشاط ← توقيع 6م ← نشاط تاني 8م بعد التوقيع
            await SeedActivityAsync(Yesterday.AddHours(9));
            await SignOffAtAsync(Yesterday, Yesterday.AddHours(18));
            await SeedActivityAsync(Yesterday.AddHours(20));

            var pending = await UnsignedDatesAsync();

            Assert.Contains(Yesterday, pending);
        }

        [Fact]
        public async Task APastDaySignedAfterAllItsActivity_IsNotFlagged()
        {
            await EstablishBaselineSignOffAsync(Today.AddDays(-4));

            await SeedActivityAsync(Yesterday.AddHours(9));
            await SignOffAtAsync(Yesterday, Yesterday.AddHours(23));

            var pending = await UnsignedDatesAsync();

            Assert.DoesNotContain(Yesterday, pending);
        }

        [Fact]
        public async Task SigningOff_WithAWrongPassword_IsRefused()
        {
            await SetPasswordAsync();

            using var scope = _db.CreateScope();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.GetService<DailyOperationsSignOffService>(scope).SignOffAsync(Today, "wrong"));

            Assert.Contains("غلط", ex.Message);
            Assert.False(await _db.GetService<DailyOperationsSignOffService>(scope).IsSignedOffAsync(Today));
        }

        // ======================= GetUnsignedPastDatesAsync =======================

        [Fact]
        public async Task FirstCallOnAFreshDatabase_SeedsYesterdayAndReportsNothingPending()
        {
            // مفيش أي توقيع اتعمل قبل كده، ومفيش نشاط خالص — أول استدعاء
            // لازم يبذر "أمس" كموقّع تلقائي ويرجّع قايمة فاضية، مش يفضح
            // سنين من التاريخ القديم
            var pending = await UnsignedDatesAsync();

            Assert.Empty(pending);

            using var scope = _db.CreateScope();
            Assert.True(await _db.GetService<DailyOperationsSignOffService>(scope).IsSignedOffAsync(Yesterday));
        }

        [Fact]
        public async Task ActivityOnTheSeededCutoverDateItself_IsNotFlagged()
        {
            // البذرة التلقائية بتعتبر "أمس" موقّع — نشاط في نفس اليوم ده
            // (اللي هو حد البذرة بالظبط) میفترضش يظهر في قايمة اللحاق
            await SeedActivityAsync(Yesterday);

            var pending = await UnsignedDatesAsync();

            Assert.Empty(pending);
        }

        [Fact]
        public async Task ActivityToday_IsExcludedFromThePendingList()
        {
            // النهارده لسه شغال — بيتوقّع عند الإغلاق، مش في ديالوج اللحاق
            await SeedActivityAsync(Today);

            var pending = await UnsignedDatesAsync();

            Assert.Empty(pending);
        }

        [Fact]
        public async Task ActivityOnAPastUnsignedDay_IsFlagged()
        {
            await EstablishBaselineSignOffAsync(Today.AddDays(-4));
            await SeedActivityAsync(TwoDaysAgo);

            var pending = await UnsignedDatesAsync();

            Assert.Equal(new[] { TwoDaysAgo }, pending);
        }

        [Fact]
        public async Task ActivityAcrossSeveralPastDays_ReturnsEachDistinctDateOnce()
        {
            await EstablishBaselineSignOffAsync(Today.AddDays(-4));
            await SeedActivityAsync(ThreeDaysAgo);
            await SeedActivityAsync(ThreeDaysAgo.AddHours(3)); // نفس اليوم، وقت تاني
            await SeedActivityAsync(TwoDaysAgo);

            var pending = await UnsignedDatesAsync();

            Assert.Equal(new[] { ThreeDaysAgo, TwoDaysAgo }, pending);
        }

        [Fact]
        public async Task ADayAlreadySignedOff_IsNeverFlaggedAgain()
        {
            await SeedActivityAsync(TwoDaysAgo);

            using (var scope = _db.CreateScope())
                await _db.GetService<DailyOperationsSignOffService>(scope)
                    .AcknowledgeLateAsync(new[] { TwoDaysAgo }, "");

            var pending = await UnsignedDatesAsync();

            Assert.Empty(pending);
        }

        // ======================= AcknowledgeLateAsync =======================

        [Fact]
        public async Task AcknowledgingLateDays_SignsOffAllOfThemWithOnePasswordCheck()
        {
            // الباسورد لازم يتحدد بعد التوقيع الأساسي مباشرة — تحديد
            // الباسورد قبل كده كان هيخلي SignOffAsync("") بتاع الأساس
            // يتاخد كباسورد غلط بدل ما يعدّي على بوابة مفتوحة
            await EstablishBaselineSignOffAsync(Today.AddDays(-4));
            await SetPasswordAsync();
            await SeedActivityAsync(ThreeDaysAgo);
            await SeedActivityAsync(TwoDaysAgo);

            var pendingBefore = await UnsignedDatesAsync();
            Assert.Equal(2, pendingBefore.Count);

            using (var scope = _db.CreateScope())
                await _db.GetService<DailyOperationsSignOffService>(scope)
                    .AcknowledgeLateAsync(pendingBefore, Password);

            using var check = _db.CreateScope();
            var service = _db.GetService<DailyOperationsSignOffService>(check);
            Assert.True(await service.IsSignedOffAsync(ThreeDaysAgo));
            Assert.True(await service.IsSignedOffAsync(TwoDaysAgo));

            // فيه توقيع تالت (الأساس، Today.AddDays(-4)) مسجّل قبل كده —
            // بنفلتر على "لاحق" عشان نتأكد من توقيعي اللحاق بالظبط
            var logged = await _db.GetService<AppDbContext>(check).ActivityEvents
                .AsNoTracking()
                .Where(e => e.EventType == ActivityEventType.DaySignedOff && e.Details!.Contains("لاحق"))
                .ToListAsync();
            Assert.Equal(2, logged.Count);
        }

        [Fact]
        public async Task AcknowledgingAnEmptyList_DoesNothing()
        {
            using var scope = _db.CreateScope();
            await _db.GetService<DailyOperationsSignOffService>(scope)
                .AcknowledgeLateAsync(Array.Empty<DateTime>(), "");

            Assert.Empty(await _db.GetService<AppDbContext>(scope).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == ActivityEventType.DaySignedOff).ToListAsync());
        }
    }
}
