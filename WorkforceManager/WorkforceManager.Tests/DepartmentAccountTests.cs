using Microsoft.EntityFrameworkCore;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;
using Xunit;

namespace WorkforceManager.Tests
{
    /// <summary>
    /// الحسابات الإدارية (مدير/رئيس قسم): تصنيف Worker.HourlyRole جديد
    /// (DepartmentManager/DepartmentHead) مستبعد تمامًا من
    /// GetActiveWithSkillsAsync/GetAllWithSkillsAsync (فمش بيظهر في شاشة
    /// العمال ولا التقارير ولا رحلة الإنتاج)، وليه قايمته الخاصة
    /// (GetDepartmentAccountsAsync) وتعبية حضور تلقائية
    /// (DepartmentAttendanceService.EnsureDailyPresenceAsync) بدل ما
    /// يحتاج أي فعل من المستخدم.
    /// </summary>
    public class DepartmentAccountTests : IDisposable
    {
        private readonly TestDatabase _db = new();

        public void Dispose() => _db.Dispose();

        private async Task<int> CreateDepartmentAccountAsync(
            string name = "مدير الإنتاج", HourlyRole role = HourlyRole.DepartmentManager,
            decimal dailyWage = 300m)
        {
            using var scope = _db.CreateScope();
            var worker = await _db.GetService<WorkerManagementService>(scope)
                .CreateWorkerAsync(name, hourlyRole: role, dailyWageEgp: dailyWage);
            return worker.Id;
        }

        private async Task SetCreatedAtAsync(int workerId, DateTime createdAt)
        {
            using var scope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(scope);
            var worker = await db.Workers.FindAsync(workerId);
            worker!.CreatedAt = createdAt;
            await db.SaveChangesAsync();
        }

        // ======================= الاستثناء المركزي =======================

        [Fact]
        public async Task GetActiveWithSkillsAsync_ExcludesDepartmentAccounts()
        {
            var accountId = await CreateDepartmentAccountAsync();

            using var scope = _db.CreateScope();
            var workers = await _db.GetService<IWorkerRepository>(scope).GetActiveWithSkillsAsync();

            Assert.DoesNotContain(workers, w => w.Id == accountId);
            Assert.Contains(workers, w => w.Id == TestDatabase.WorkerAhmedId); // العمال العاديين لسه ظاهرين
        }

        [Fact]
        public async Task GetAllWithSkillsAsync_ExcludesDepartmentAccountsEvenIfInactive()
        {
            var accountId = await CreateDepartmentAccountAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).DeactivateWorkerAsync(accountId);

            using var checkScope = _db.CreateScope();
            var workers = await _db.GetService<IWorkerRepository>(checkScope).GetAllWithSkillsAsync();

            Assert.DoesNotContain(workers, w => w.Id == accountId);
        }

        [Fact]
        public async Task GetDepartmentAccountsAsync_ReturnsOnlyDepartmentAccounts()
        {
            var managerId = await CreateDepartmentAccountAsync("مدير القسم", HourlyRole.DepartmentManager);
            var headId = await CreateDepartmentAccountAsync("رئيس القسم", HourlyRole.DepartmentHead);

            using var scope = _db.CreateScope();
            var accounts = await _db.GetService<IWorkerRepository>(scope).GetDepartmentAccountsAsync();

            Assert.Equal(2, accounts.Count);
            Assert.Contains(accounts, w => w.Id == managerId);
            Assert.Contains(accounts, w => w.Id == headId);
            Assert.DoesNotContain(accounts, w => w.Id == TestDatabase.WorkerAhmedId);
        }

        // ======================= التعبية التلقائية =======================

        [Fact]
        public async Task EnsureDailyPresenceAsync_BackfillsEveryDayFromCreationToToday()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, DateTime.Today.AddDays(-2));

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);

            for (var day = DateTime.Today.AddDays(-2); day <= DateTime.Today; day = day.AddDays(1))
            {
                var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == day);
                Assert.NotNull(log);
                Assert.Equal(1.0m, log!.WorkdaysCredited);

                var attendance = await db.Attendances.FirstOrDefaultAsync(a => a.WorkerId == accountId && a.Date == day);
                Assert.NotNull(attendance);
                Assert.Equal(Core.Enums.AttendanceStatus.Present, attendance!.Status);
            }
        }

        [Fact]
        public async Task EnsureDailyPresenceAsync_NeverOverwritesADayTheManagerAlreadyRecorded()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, DateTime.Today);

            // المدير سجّل سهر اليوم بإيده (يومية ونص) قبل ما التعبية تتنادى
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, DateTime.Today, HourlyWorkdayService.EveningEndHour);

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);
            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == DateTime.Today);

            Assert.NotNull(log);
            Assert.Equal(1.5m, log!.WorkdaysCredited); // مفيش استبدال — فضل زي ما المدير سجّله
        }

        [Fact]
        public async Task EnsureDailyPresenceAsync_SkipsDeactivatedAccounts()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, DateTime.Today);

            using (var scope = _db.CreateScope())
                await _db.GetService<WorkerManagementService>(scope).DeactivateWorkerAsync(accountId);

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);
            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == DateTime.Today);

            Assert.Null(log);
        }

        [Fact]
        public async Task EnsureDailyPresenceAsync_IsIdempotent()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, DateTime.Today.AddDays(-1));

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);
            var logs = await db.HourlyWorkLogs.Where(h => h.WorkerId == accountId).ToListAsync();

            Assert.Equal(2, logs.Count); // يومين بس (النهارده وامبارح) — مفيش تكرار
        }

        // ======================= تصحيح يوم يدويًا (بروفايل الحساب) =======================

        [Fact]
        public async Task CorrectDayAsync_Present_RecordsTheGivenShiftAndMarksPresent()
        {
            var accountId = await CreateDepartmentAccountAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope)
                    .CorrectDayAsync(accountId, TestDatabase.Today, AttendanceStatus.Present, HourlyWorkdayService.EveningEndHour);

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);

            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == TestDatabase.Today);
            Assert.NotNull(log);
            Assert.Equal(1.5m, log!.WorkdaysCredited); // سهر

            var attendance = await db.Attendances.FirstOrDefaultAsync(a => a.WorkerId == accountId && a.Date == TestDatabase.Today);
            Assert.NotNull(attendance);
            Assert.Equal(AttendanceStatus.Present, attendance!.Status);
        }

        [Fact]
        public async Task CorrectDayAsync_Absent_RemovesTheExistingHourlyWorkLogAndMarksAbsent()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, TestDatabase.Today);

            // التعبية التلقائية سجّلت يومية حاضر — زي أي حساب إداري عادي
            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope)
                    .CorrectDayAsync(accountId, TestDatabase.Today, AttendanceStatus.AbsentWithoutPermission, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);

            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == TestDatabase.Today);
            Assert.Null(log); // اتشال — نفس قاعدة "الغياب مع شغل مسجّل ممنوع"

            var attendance = await db.Attendances.FirstOrDefaultAsync(a => a.WorkerId == accountId && a.Date == TestDatabase.Today);
            Assert.NotNull(attendance);
            Assert.Equal(AttendanceStatus.AbsentWithoutPermission, attendance!.Status);
        }

        [Fact]
        public async Task CorrectDayAsync_Absent_ThenNextBackfill_DoesNotReinstatePresence()
        {
            var accountId = await CreateDepartmentAccountAsync();
            await SetCreatedAtAsync(accountId, TestDatabase.Today);

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope)
                    .CorrectDayAsync(accountId, TestDatabase.Today, AttendanceStatus.AbsentWithPermission, HourlyWorkdayService.ShiftEndHour);

            // نفس التعبية بتتنادى تاني (زي فتح البرنامج تاني يوم) — لازم
            // تسيب الغياب المسجّل بإيد المدير زي ما هو، مش تحسبه حاضر تاني
            using (var scope = _db.CreateScope())
                await _db.GetService<DepartmentAttendanceService>(scope).EnsureDailyPresenceAsync();

            using var checkScope = _db.CreateScope();
            var db = _db.GetService<AppDbContext>(checkScope);

            var log = await db.HourlyWorkLogs.FirstOrDefaultAsync(h => h.WorkerId == accountId && h.Date == TestDatabase.Today);
            Assert.Null(log); // التعبية ما رجعتش تحط يومية

            var attendance = await db.Attendances.FirstOrDefaultAsync(a => a.WorkerId == accountId && a.Date == TestDatabase.Today);
            Assert.Equal(AttendanceStatus.AbsentWithPermission, attendance!.Status); // الغياب فضل زي ما هو
        }

        // ======================= مفيش خلط مع تقارير العمال =======================

        [Fact]
        public async Task PeriodPayroll_ExcludesDepartmentAccounts_EvenWithRealAttendanceAndHourlyLogs()
        {
            var accountId = await CreateDepartmentAccountAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var payroll = await _db.GetService<PayrollService>(checkScope)
                .GetPeriodPayrollAsync(TestDatabase.Today, TestDatabase.Today);

            Assert.DoesNotContain(payroll.Workers, w => w.WorkerId == accountId);
        }

        [Fact]
        public async Task WeeklySummary_ExcludesDepartmentAccounts()
        {
            // يومية بأجر عالي عشان لو الاستثناء فشل هيبان فورًا في أي فرز بالصافي
            var accountId = await CreateDepartmentAccountAsync("مدير عالي الأجر", dailyWage: 100000m);
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var summary = await _db.GetService<WeeklySummaryService>(checkScope)
                .GetTeamWeeklySummaryAsync(TestDatabase.Today);

            Assert.DoesNotContain(summary, s => s.WorkerId == accountId);
        }

        [Fact]
        public async Task ReportBuilder_AttendanceSubject_ExcludesDepartmentAccounts()
        {
            var accountId = await CreateDepartmentAccountAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.Attendance,
                GroupBy = ReportGrouping.Worker,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            // العاملة/المدير مالوش أي عامل عادي بحضور مسجل في اليوم ده —
            // لو الاستثناء فشل هيبان صف "مدير الإنتاج"، والصحيح مفيش صفوف خالص
            Assert.Empty(table.Rows);
        }

        // ======================= تقرير الحسابات الإدارية =======================

        [Fact]
        public async Task DepartmentAccountsReport_GroupedByWorker_ShowsPresenceWorkdaysAndWage()
        {
            // رئيس قسم عشان الأجر يبان في التقرير — مدير القسم مالوش
            // راتب خالص (شوف DepartmentAccountsReport_ManagerWage_IsAlwaysNull)
            var accountId = await CreateDepartmentAccountAsync("رئيس الإنتاج", HourlyRole.DepartmentHead, dailyWage: 300m);
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, TestDatabase.Today, HourlyWorkdayService.EveningEndHour); // سهر: 1.5 يومية

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Worker,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal("رئيس الإنتاج (رئيس قسم)", row.Label);
            Assert.Equal(1, row.Values[0]);
            Assert.Equal(1.5m, row.Values[1]);
            Assert.Equal(450m, row.Values[2]); // 1.5 × 300
        }

        [Fact]
        public async Task DepartmentAccountsReport_GroupedByDay_AggregatesAcrossAccounts()
        {
            var managerId = await CreateDepartmentAccountAsync("مدير القسم", HourlyRole.DepartmentManager, 200m);
            var headId = await CreateDepartmentAccountAsync("رئيس القسم", HourlyRole.DepartmentHead, 250m);

            using (var scope = _db.CreateScope())
            {
                var hourly = _db.GetService<HourlyWorkdayService>(scope);
                await hourly.RecordHourlyWorkAsync(managerId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);
                await hourly.RecordHourlyWorkAsync(headId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);
            }

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Day,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal(2, row.Values[0]); // يومين حضور (الاتنين)
            Assert.Equal(2.0m, row.Values[1]); // يومية كاملة لكل واحد
            Assert.Equal(250m, row.Values[2]); // مدير القسم مالوش راتب — رئيس القسم بس (250)
        }

        [Fact]
        public async Task DepartmentAccountsReport_ManagerWage_IsAlwaysNull_EvenIfDailyWageEgpIsSetInTheDatabase()
        {
            // لو حد حط راتب لمدير القسم بأي طريق تاني غير شاشة التعديل
            // (اللي بتقفل الحقل ده)، التقرير برضه لازم يعرضه فاضي —
            // الدفاع مش لازم يعتمد على واجهة واحدة بس
            var managerId = await CreateDepartmentAccountAsync("مدير القسم", HourlyRole.DepartmentManager, dailyWage: 300m);
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(managerId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Worker,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal(1.0m, row.Values[1]); // اليومية اتحسبت عادي
            Assert.Null(row.Values[2]); // بس الأجر مالوش قيمة خالص — مش صفر
        }

        [Fact]
        public async Task DepartmentAccountsReport_GroupedByWorker_LabelIncludesRoleName()
        {
            var headId = await CreateDepartmentAccountAsync("رئيس القسم", HourlyRole.DepartmentHead, dailyWage: 250m);
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(headId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Worker,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal("رئيس القسم (رئيس قسم)", row.Label);
            Assert.Equal(250m, row.Values[2]);
        }

        [Fact]
        public async Task DepartmentAccountsReport_GroupedByMonth_AggregatesTheWholeMonth()
        {
            var headId = await CreateDepartmentAccountAsync("رئيس القسم", HourlyRole.DepartmentHead, dailyWage: 300m);

            using (var scope = _db.CreateScope())
            {
                var hourly = _db.GetService<HourlyWorkdayService>(scope);
                await hourly.RecordHourlyWorkAsync(headId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour); // يومية
                await hourly.RecordHourlyWorkAsync(headId, TestDatabase.Today.AddDays(-1), HourlyWorkdayService.EveningEndHour); // يومية ونص
            }

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Month,
                From = TestDatabase.Today.AddDays(-1),
                To = TestDatabase.Today
            });

            var row = Assert.Single(table.Rows);
            Assert.Equal(2, row.Values[0]); // يومين حضور
            Assert.Equal(2.5m, row.Values[1]); // 1 + 1.5
            Assert.Equal(750m, row.Values[2]); // 2.5 × 300
        }

        [Fact]
        public async Task DepartmentAccountsReport_NeverIncludesARegularWorker()
        {
            var accountId = await CreateDepartmentAccountAsync();
            using (var scope = _db.CreateScope())
                await _db.GetService<HourlyWorkdayService>(scope)
                    .RecordHourlyWorkAsync(accountId, TestDatabase.Today, HourlyWorkdayService.ShiftEndHour);

            using var checkScope = _db.CreateScope();
            var table = await _db.GetService<ReportBuilderService>(checkScope).BuildAsync(new ReportSpec
            {
                Subject = ReportSubject.DepartmentAccounts,
                GroupBy = ReportGrouping.Worker,
                From = TestDatabase.Today,
                To = TestDatabase.Today
            });

            Assert.DoesNotContain(table.Rows, r => r.Label == "أحمد");
            Assert.DoesNotContain(table.Rows, r => r.Label == "سعيد");
        }
    }
}
