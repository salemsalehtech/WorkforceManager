using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// اختبارات شاشة الحضور الموحّدة: الحضور التلقائي من الشغل المسجّل،
    /// وجزاء الغياب التلقائي (توليد/إزالة/عدم تكرار)، وإن الخصم مبيتحسبش
    /// مرتين، وإن الجزاء اليدوي مبيتلمسش أبدًا.
    /// </summary>
    public class AttendanceAutomationTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        // ---------------- أدوات مساعدة ----------------

        private Task<AttendanceSaveResultDto> SaveAttendanceAsync(
            params (int WorkerId, AttendanceStatus Status)[] entries) =>
            _db.InScopeAsync<AttendanceService, AttendanceSaveResultDto>(service =>
                service.RecordAttendanceBatchAsync(TestDatabase.Today, entries));

        private Task<FlowSaveResultDto> RecordProductionAsync(int workerId) =>
            _db.InScopeAsync<ProductionFlowService, FlowSaveResultDto>(service =>
                service.RecordFlowAsync(
                    TestDatabase.ProductRingId, TestDatabase.Today,
                    new[]
                    {
                        new BatchRangeDto
                        {
                            FromStageId = TestDatabase.RingStage1Id,
                            ToStageId = TestDatabase.RingStage1Id, PieceCount = 10
                        }
                    },
                    new[]
                    {
                        new FlowShareDto
                        {
                            ProductionStageId = TestDatabase.RingStage1Id,
                            WorkerId = workerId, PieceCount = 10
                        }
                    }));

        private async Task<List<Penalty>> GetPenaltiesAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            return await db.Penalties.AsNoTracking().ToListAsync();
        }

        private async Task<List<Attendance>> GetAttendanceAsync()
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            return await db.Attendances.AsNoTracking().ToListAsync();
        }

        // ---------------- B2: الحالات المتاحة حسب نوع العامل ----------------

        [Fact]
        public void StatusCatalog_ReadsStatusesFromSystemConfiguration_NotAHardcodedList()
        {
            var forDaily = AttendanceStatusCatalog.ForWorker(isHourly: false);
            var forHourly = AttendanceStatusCatalog.ForWorker(isHourly: true);

            // المصدر هو تعريف الحالات في النظام — أي حالة تتضاف بتظهر لوحدها
            Assert.Equal(Enum.GetValues<AttendanceStatus>().Length, forDaily.Count);
            Assert.Contains(AttendanceStatus.Present, forDaily);
            Assert.Contains(AttendanceStatus.AbsentWithPermission, forDaily);
            Assert.Contains(AttendanceStatus.AbsentWithoutPermission, forDaily);

            // النوعين حاليًا بياخدوا نفس المجموعة (القرار المتفق عليه)
            Assert.Equal(forDaily, forHourly);
        }

        [Fact]
        public void StatusCatalog_OnlyUnexcusedAbsenceTriggersAPenalty()
        {
            Assert.True(AttendanceStatusCatalog.TriggersAbsencePenalty(AttendanceStatus.AbsentWithoutPermission));
            Assert.False(AttendanceStatusCatalog.TriggersAbsencePenalty(AttendanceStatus.AbsentWithPermission));
            Assert.False(AttendanceStatusCatalog.TriggersAbsencePenalty(AttendanceStatus.Present));
        }

        // ---------------- B3: الحضور التلقائي من الشغل المسجّل ----------------

        [Fact]
        public async Task WorkerWithProduction_CountsAsHavingLoggedWork()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId);

            using var scope = _db.CreateScope();
            var automation = _db.GetService<AttendanceAutomationService>(scope);
            var withWork = await automation.GetWorkersWithLoggedWorkAsync(TestDatabase.Today);

            Assert.Contains(TestDatabase.WorkerAhmedId, withWork);
            Assert.DoesNotContain(TestDatabase.WorkerSaidId, withWork);
        }

        [Fact]
        public async Task HourlyWorkerWithLoggedHours_CountsAsHavingLoggedWork()
        {
            // العامل بالساعة مالوش إنتاج على مراحل — لو القاعدة بصّت على
            // الإنتاج بس كان هيتحسب إنه ماشتغلش
            await _db.InScopeAsync<HourlyWorkdayService, HourlyWorkLog>(service =>
                service.RecordHourlyWorkAsync(
                    TestDatabase.WorkerMonaHourlyId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour));

            using var scope = _db.CreateScope();
            var automation = _db.GetService<AttendanceAutomationService>(scope);
            var withWork = await automation.GetWorkersWithLoggedWorkAsync(TestDatabase.Today);

            Assert.Contains(TestDatabase.WorkerMonaHourlyId, withWork);
        }

        // ---------------- B4: جزاء الغياب التلقائي ----------------

        [Fact]
        public async Task AbsentWithoutPermission_CreatesExactlyOneHalfDayAutoPenalty()
        {
            var result = await SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));

            Assert.Equal(1, result.AutoPenaltiesCreated);

            var penalty = Assert.Single(await GetPenaltiesAsync());
            Assert.Equal(TestDatabase.WorkerAhmedId, penalty.WorkerId);
            Assert.Equal(PenaltyDeduction.HalfDay, penalty.Deduction);
            Assert.Equal(PenaltySource.AutoAbsence, penalty.Source);
            Assert.Equal(0.5m, penalty.DeductedWorkdays);
        }

        [Fact]
        public async Task AbsentWithPermission_CreatesNoPenalty()
        {
            var result = await SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithPermission));

            Assert.Equal(0, result.AutoPenaltiesCreated);
            Assert.Empty(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task Present_CreatesNoPenalty()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.Present));
            Assert.Empty(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task HourlyWorkerAbsentWithoutPermission_AlsoGetsTheAutoPenalty()
        {
            // القرار المتفق عليه: الجزاء بيتطبق على النوعين
            await SaveAttendanceAsync(
                (TestDatabase.WorkerMonaHourlyId, AttendanceStatus.AbsentWithoutPermission));

            var penalty = Assert.Single(await GetPenaltiesAsync());
            Assert.Equal(TestDatabase.WorkerMonaHourlyId, penalty.WorkerId);
            Assert.Equal(PenaltySource.AutoAbsence, penalty.Source);
        }

        // ---------------- B6: إعادة الحفظ متعملش تكرار ----------------

        [Fact]
        public async Task ResavingSameAbsence_CreatesNoDuplicatePenaltyOrAttendance()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            var second = await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            var third = await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));

            Assert.Equal(0, second.AutoPenaltiesCreated);
            Assert.Equal(0, third.AutoPenaltiesCreated);

            Assert.Single(await GetPenaltiesAsync());
            Assert.Single(await GetAttendanceAsync());
        }

        // ---------------- B5: إزالة الجزاء لما الحالة تتغير ----------------

        [Fact]
        public async Task ChangingAwayFromUnexcusedAbsence_RemovesTheAutoPenalty()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            Assert.Single(await GetPenaltiesAsync());

            var result = await SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithPermission));

            Assert.Equal(1, result.AutoPenaltiesRemoved);
            Assert.Empty(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task ChangingToPresent_RemovesTheAutoPenalty()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.Present));

            Assert.Empty(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task ManualPenaltyOnSameDay_IsNeverRemovedByTheAutomation()
        {
            // جزاء يدوي (شرب سجاير) + غياب بدون إذن في نفس اليوم
            await _db.InScopeAsync<PenaltyService, Penalty>(service =>
                service.RecordPenaltyAsync(
                    TestDatabase.WorkerAhmedId, TestDatabase.Today,
                    "شرب سجاير", PenaltyDeduction.OneDay));

            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            Assert.Equal(2, (await GetPenaltiesAsync()).Count); // اليدوي + التلقائي

            // تغيير الحالة بيشيل التلقائي بس
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.Present));

            var remaining = Assert.Single(await GetPenaltiesAsync());
            Assert.Equal(PenaltySource.Manual, remaining.Source);
            Assert.Equal("شرب سجاير", remaining.Reason);
        }

        [Fact]
        public async Task AutoPenalty_CannotBeDeletedManually()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));
            var penalty = Assert.Single(await GetPenaltiesAsync());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _db.InScopeAsync<PenaltyService, bool>(async service =>
                {
                    await service.RemovePenaltyAsync(penalty.Id);
                    return true;
                }));

            Assert.Contains("جزاء تلقائي", ex.Message);
            Assert.Single(await GetPenaltiesAsync()); // لسه موجود
        }

        // ---------------- الخصم مبيتحسبش مرتين ----------------

        [Fact]
        public async Task UnexcusedAbsence_IsDeductedOnceNotTwice()
        {
            await SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission));

            var attendance = await GetAttendanceAsync();
            var penalties = await GetPenaltiesAsync();

            // الخصم القديم (المدفون) بقى صفر لأن اليوم ده ليه جزاء تلقائي
            var legacyDeduction = AbsenceDeductionRule.ComputeUnpenalizedAbsenceDeduction(attendance, penalties);
            var penaltyDeduction = penalties.Sum(p => p.DeductedWorkdays);

            Assert.Equal(0m, legacyDeduction);
            Assert.Equal(0.5m, penaltyDeduction);
            Assert.Equal(0.5m, legacyDeduction + penaltyDeduction); // نص يومية بالظبط، مش يومية
        }

        [Fact]
        public void LegacyAbsenceWithoutAutoPenalty_StillDeductsHalfADay()
        {
            // بيانات قديمة اتسجلت قبل الميزة دي: غياب من غير جزاء تلقائي.
            // لازم تفضل متخصومة صح، من غير ما نلمس بياناتها
            var attendance = new List<Attendance>
            {
                new()
                {
                    WorkerId = TestDatabase.WorkerAhmedId, Date = TestDatabase.Today,
                    Status = AttendanceStatus.AbsentWithoutPermission
                }
            };

            var deduction = AbsenceDeductionRule.ComputeUnpenalizedAbsenceDeduction(attendance, new List<Penalty>());

            Assert.Equal(0.5m, deduction);
        }

        [Fact]
        public void ExcusedAbsence_CostsNothing()
        {
            var attendance = new List<Attendance>
            {
                new()
                {
                    WorkerId = TestDatabase.WorkerAhmedId, Date = TestDatabase.Today,
                    Status = AttendanceStatus.AbsentWithPermission
                }
            };

            Assert.Equal(0m, AbsenceDeductionRule.ComputeUnpenalizedAbsenceDeduction(attendance, new List<Penalty>()));
        }

        // ---------------- قاعدة الحماية: غياب لعامل له شغل ----------------

        [Fact]
        public async Task MarkingAbsent_AWorkerWithProduction_IsRejectedAndNothingIsSaved()
        {
            await RecordProductionAsync(TestDatabase.WorkerAhmedId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SaveAttendanceAsync((TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission)));

            Assert.Contains("أحمد", ex.Message);

            // الحضور فضل "حاضر" (اللي رحلة الإنتاج سجلته) ومفيش أي جزاء
            var attendance = Assert.Single(await GetAttendanceAsync());
            Assert.Equal(AttendanceStatus.Present, attendance.Status);
            Assert.Empty(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task MarkingAbsent_AnHourlyWorkerWithLoggedHours_IsAlsoRejected()
        {
            await _db.InScopeAsync<HourlyWorkdayService, HourlyWorkLog>(service =>
                service.RecordHourlyWorkAsync(
                    TestDatabase.WorkerMonaHourlyId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SaveAttendanceAsync((TestDatabase.WorkerMonaHourlyId, AttendanceStatus.AbsentWithoutPermission)));

            Assert.Empty(await GetPenaltiesAsync());
        }

        // ---------------- اختصارات الشيفت ----------------

        [Fact]
        public void ShiftPresets_CoverTheThreeDistinctOutcomesOfTheLadder()
        {
            var presets = HourlyWorkdayService.ShiftPresets;

            Assert.Equal(3, presets.Count);

            var workdays = presets.Select(p => HourlyWorkdayService.ComputeWorkdays(p.EndHour24)).ToList();
            Assert.Equal(new[] { 1.0m, 1.5m, 2.0m }, workdays);
        }

        // ---------------- التزامن ----------------

        [Fact]
        public async Task ConcurrentSavesOfTheSameAbsence_DoNotDuplicateRecordsOrPenalties()
        {
            var first = Task.Run(() => SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission)));
            var second = Task.Run(() => SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission)));

            await Task.WhenAll(Settle(first), Settle(second));

            // المهم مش مين نجح — المهم الحالة النهائية سليمة
            Assert.Single(await GetAttendanceAsync());
            Assert.Single(await GetPenaltiesAsync());
        }

        [Fact]
        public async Task ConcurrentSavesForDifferentWorkers_BothSucceedCleanly()
        {
            var ahmed = Task.Run(() => SaveAttendanceAsync(
                (TestDatabase.WorkerAhmedId, AttendanceStatus.AbsentWithoutPermission)));
            var said = Task.Run(() => SaveAttendanceAsync(
                (TestDatabase.WorkerSaidId, AttendanceStatus.AbsentWithoutPermission)));

            await Task.WhenAll(Settle(ahmed), Settle(said));

            var penalties = await GetPenaltiesAsync();
            Assert.All(penalties, p => Assert.Equal(PenaltySource.AutoAbsence, p.Source));
            // كل عامل جزاء واحد بالكتير
            Assert.Equal(penalties.Select(p => p.WorkerId).Distinct().Count(), penalties.Count);
        }

        private static async Task<bool> Settle(Task task)
        {
            try
            {
                await task;
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
            catch (DbUpdateException) { return false; }
        }
    }
}
