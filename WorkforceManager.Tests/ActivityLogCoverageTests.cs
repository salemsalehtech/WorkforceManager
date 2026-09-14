using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// سجل العمليات لازم يسجّل **كل عملية ليها قيمة**، مش الحذف بس.
    ///
    /// ست أنواع أحداث كانت معرّفة في القايمة وليها أسماء عربية وسياسة
    /// احتفاظ — و**محدش كان بيكتبها**. يعني الشاشة كانت بتعرض عمليات
    /// الحذف بس، والمستخدم اللي بيدوّر "مين غيّر أجر العامل ده؟"
    /// مبيلاقيش حاجة مع إن البرنامج شكله بيسجّلها.
    ///
    /// الاختبارات هنا بتمسك النوع ده من العطل: نوع حدث موجود ومفيش
    /// مسار بيكتبه. كل اختبار بيعمل العملية الحقيقية وبيدوّر على حدثها.
    /// </summary>
    public class ActivityLogCoverageTests : IDisposable
    {
        private const string Password = "1234";

        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private static DateTime Day => TestDatabase.Today;

        private async Task SetPasswordAsync()
        {
            await _db.SignInTestUserAsync();

            using var scope = _db.CreateScope();
            await _db.GetService<OperationsPasswordService>(scope).SetPasswordAsync(null, Password);
        }

        /// <summary>أحداث نوع معيّن اتكتبت في السجل</summary>
        private async Task<List<Core.Models.ActivityEvent>> EventsOfAsync(ActivityEventType type)
        {
            using var scope = _db.CreateScope();
            return await _db.GetService<AppDbContext>(scope).ActivityEvents
                .AsNoTracking().Where(e => e.EventType == type).ToListAsync();
        }

        // ---------------- الفلوس ----------------

        [Fact]
        public async Task ChangingAWorkerWage_IsLogged()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                    TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: 250);

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.WorkerWageChanged));
            Assert.Contains("250", logged.Details);
        }

        [Fact]
        public async Task ChangingTheNameWithoutTheWage_IsNotLogged()
        {
            // السجل بيحكي حركات الفلوس. تعديل اسم مش حركة فلوس، ولو
            // اتسجّل كل تعديل بيبقى ضوضاء بتخفي اللي بيتسأل عنه
            decimal wage;
            using (var scope = _db.CreateScope())
                wage = (await _db.GetService<Core.Interfaces.IWorkerRepository>(scope)
                    .GetByIdAsync(TestDatabase.WorkerAhmedId))!.DailyWageEgp;

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                    TestDatabase.WorkerAhmedId, "اسم جديد", dailyWageEgp: wage);

            Assert.Empty(await EventsOfAsync(ActivityEventType.WorkerWageChanged));
        }

        [Fact]
        public async Task RecordingAnAdvance_IsLogged()
        {
            await SetPasswordAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WageAdjustmentService>(scope).RecordAdjustmentAsync(
                    TestDatabase.WorkerAhmedId, Day, WageAdjustmentType.Advance, 500,
                    operationsPassword: Password);

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.WageAdjustmentSaved));
            Assert.Equal("سلفة", logged.EntityName);
            Assert.Contains("500", logged.Details);
        }

        [Fact]
        public async Task RecordingAPenalty_IsLogged_ButTheAutomaticOneIsNot()
        {
            await SetPasswordAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerAhmedId, Day, "اتأخر", PenaltyDeduction.HalfDay);

            // الجزاء التلقائي انعكاس لحالة الحضور اللي اتسجّلت خلاص —
            // تسجيله بيبقى نفس الحدث مرتين
            using (var scope = _db.CreateScope())
                await _db.GetService<PenaltyService>(scope).RecordPenaltyAsync(
                    TestDatabase.WorkerSaidId, Day, "غياب", PenaltyDeduction.HalfDay,
                    source: PenaltySource.AutoAbsence);

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.PenaltySaved));
            Assert.Equal("اتأخر", logged.EntityName);
        }

        [Fact]
        public async Task CorrectingThePieceCount_IsLogged()
        {
            await SetPasswordAsync();

            int recordId;
            using (var scope = _db.CreateScope())
                recordId = (await _db.GetService<WorkdayCalculationService>(scope)
                    .RecordProductionAsync(TestDatabase.WorkerAhmedId, TestDatabase.BagStage1Id,
                        100, Day, confirmOverride: true)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkdayCalculationService>(scope)
                    .UpdateProductionAsync(recordId, 150);

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.ProductionPiecesEdited));
            Assert.Contains("100", logged.Details);
            Assert.Contains("150", logged.Details);
        }

        [Fact]
        public async Task ChangingTheOperationsPassword_IsLogged()
        {
            // دي البوابة اللي بتحمي كل اللي فوق، فمين غيّرها جزء من
            // نفس السؤال
            await SetPasswordAsync();

            Assert.Single(await EventsOfAsync(ActivityEventType.OperationsPasswordChanged));
        }

        // ---------------- الشغل اليومي ----------------

        [Fact]
        public async Task RecordingScrap_IsLogged()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RecordAsync(
                    TestDatabase.BagStage1Id, Day, 300, note: "عيب خامة");

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.ScrapRecorded));
            Assert.Contains("300", logged.Details);
            Assert.Equal("عيب خامة", logged.Reason);
        }

        [Fact]
        public async Task DeletingScrap_IsLogged()
        {
            // فجوة كانت موجودة: حذف الهالك كان بيحصل من غير أي أثر في
            // السجل خالص — بقى مهم دلوقتي (Tier B، مفيش باسورد فوري)
            int scrapId;
            using (var scope = _db.CreateScope())
                scrapId = (await _db.GetService<ScrapService>(scope).RecordAsync(
                    TestDatabase.BagStage1Id, Day, 300)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<ScrapService>(scope).RemoveAsync(scrapId);

            var logged = Assert.Single(await EventsOfAsync(ActivityEventType.ScrapDeleted));
            Assert.Contains("300", logged.Details);
        }

        [Fact]
        public async Task AddingAWorkerOrAProduct_IsLogged()
        {
            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope)
                    .CreateWorkerAsync("عامل جديد", dailyWageEgp: 200);

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductManagementService>(scope)
                    .CreateProductAsync("منتج جديد");

            Assert.Equal("عامل جديد",
                Assert.Single(await EventsOfAsync(ActivityEventType.WorkerCreated)).EntityName);
            Assert.Equal("منتج جديد",
                Assert.Single(await EventsOfAsync(ActivityEventType.ProductCreated)).EntityName);
        }

        [Fact]
        public async Task CorrectingADepartmentAccountDay_IsLogged_BothPresentAndAbsent()
        {
            // فجوة كانت موجودة: تصحيح يوم حساب إداري كان بيغيّر يومية
            // مدفوعة من غير أي أثر في السجل خالص — وكان بيمر من غير ما
            // يفكّ توقيع نهاية اليوم لو اليوم ده كان موقّع (شوف
            // DailyOperationsSignOffServiceTests.CorrectingADepartmentAccountDayAfterSignOff_ReArmsTheGuard)
            int accountId;
            using (var scope = _db.CreateScope())
                accountId = (await _db.GetService<WorkerManagementService>(scope)
                    .CreateWorkerAsync("مدير الإنتاج", hourlyRole: HourlyRole.DepartmentManager, dailyWageEgp: 300m)).Id;

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope)
                    .CorrectDayAsync(accountId, Day, AttendanceStatus.Present, HourlyWorkdayService.EveningEndHour);

            var present = Assert.Single(await EventsOfAsync(ActivityEventType.DepartmentAccountDayCorrected));
            Assert.Equal("مدير الإنتاج", present.EntityName);

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope)
                    .CorrectDayAsync(accountId, Day, AttendanceStatus.AbsentWithoutPermission, HourlyWorkdayService.ShiftEndHour);

            Assert.Equal(2, (await EventsOfAsync(ActivityEventType.DepartmentAccountDayCorrected)).Count);
        }

        [Fact]
        public async Task SavingEditingAndDeletingAMemoryPlan_IsLogged()
        {
            // فجوة كانت موجودة: حفظ/تعديل/حذف خطة في "الذاكرة" كان بيحصل
            // من غير أي أثر في السجل خالص — يوم موقّع عليه بيفضل موقّع
            // حتى لو نوايا الإنتاج اتغيّرت فعلًا بعد التوقيع
            int memoryId;
            using (var scope = _db.CreateScope())
                memoryId = await _db.GetService<ProductionMemoryService>(scope).CreateAsync(
                    TestDatabase.ProductRingId,
                    new[] { TestDatabase.RingStage2Id, TestDatabase.RingStage1Id },
                    "ملاحظة", Day.AddDays(1));

            var created = Assert.Single(await EventsOfAsync(ActivityEventType.ProductionMemoryCreated));
            Assert.Equal("دبلة", created.EntityName);

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionMemoryService>(scope).UpdateAsync(
                    memoryId, TestDatabase.ProductRingId,
                    new[] { TestDatabase.RingStage1Id, TestDatabase.RingStage2Id },
                    "ملاحظة اتعدّلت", Day.AddDays(2));

            Assert.Single(await EventsOfAsync(ActivityEventType.ProductionMemoryEdited));

            // استرجاع خطة اتعلّمت منجزة غلط بيتسجّل كمان — نفس نوع التعديل
            using (var scope = _db.CreateScope())
            {
                var service = _db.GetService<ProductionMemoryService>(scope);
                await service.MarkStartedAsync(memoryId);
                await service.ReactivateAsync(memoryId);
            }

            Assert.Equal(2, (await EventsOfAsync(ActivityEventType.ProductionMemoryEdited)).Count);

            using (var scope = _db.CreateScope())
                await _db.GetService<ProductionMemoryService>(scope).DeleteAsync(memoryId);

            Assert.Single(await EventsOfAsync(ActivityEventType.ProductionMemoryDeleted));
        }

        // ---------------- سياسة الاحتفاظ ----------------

        [Fact]
        public void TheRoutineDailySaves_ExpireWithTheShortWindow()
        {
            // تسجيل الإنتاج والحضور بيحصلوا كل يوم على كل منتج — سنة
            // منهم بتغرق السجل وتخفي اللي بيتسأل عنه فعلًا
            Assert.True(ActivityEventRetention.IsShortLived(ActivityEventType.ProductionRecorded));
            Assert.True(ActivityEventRetention.IsShortLived(ActivityEventType.AttendanceSaved));

            // والفلوس بتفضل بالمدة الطويلة
            Assert.False(ActivityEventRetention.IsShortLived(ActivityEventType.WageAdjustmentSaved));
            Assert.False(ActivityEventRetention.IsShortLived(ActivityEventType.ScrapRecorded));
        }

        // ---------------- شارة "عمليات جديدة" ----------------

        [Fact]
        public async Task ActivityAfterSignIn_CountsAsUnseen()
        {
            var userId = await _db.SignInTestUserAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                    TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: 300);

            using var check = _db.CreateScope();
            var unseen = await _db.GetService<ActivityLogService>(check).GetUnseenCountAsync(userId);

            Assert.Equal(1, unseen);
        }

        [Fact]
        public async Task MarkingSeen_ClearsTheCountBackToZero()
        {
            var userId = await _db.SignInTestUserAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).UpdateWorkerAsync(
                    TestDatabase.WorkerAhmedId, "احمد", dailyWageEgp: 300);

            using (var scope = _db.CreateScope())
                await _db.GetService<ActivityLogService>(scope).MarkSeenAsync(userId);

            using var check = _db.CreateScope();
            var unseen = await _db.GetService<ActivityLogService>(check).GetUnseenCountAsync(userId);

            Assert.Equal(0, unseen);
        }

        [Fact]
        public async Task NoSignedInAccount_HasNoUnseenCount()
        {
            using var scope = _db.CreateScope();
            var unseen = await _db.GetService<ActivityLogService>(scope).GetUnseenCountAsync(null);

            Assert.Equal(0, unseen);
        }
    }
}
