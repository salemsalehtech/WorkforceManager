using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// تعبية الحضور والأجر التلقائي اليومي للحسابات الإدارية (مدير/رئيس
    /// قسم): "حاضر دائمًا من الأول" من غير أي فعل من أي مستخدم — وبيومية
    /// كاملة تلقائيًا (سعر اليومية بتاعهم × 1)، مش بس حضور بلا أجر.
    ///
    /// البرنامج مالوش نظام مهام مجدولة (Scheduled Job) — بيشتغل بس لما
    /// شاشة تُفتح أو مرة عند بدء التشغيل (نفس فكرة النسخة الاحتياطية
    /// اليومية). الحل: تعبية (Backfill) بتتنادى عند كل بدء تشغيل — لكل
    /// حساب إداري نشط، أي يوم من تاريخ إضافته لحد النهارده **لسه ملوش
    /// سجل شغل بالساعة خالص** بيتسجّل له
    /// <see cref="HourlyWorkdayService.RecordHourlyWorkAsync"/> بيومية
    /// شيفت عادي (1) — نفس الاستدعاء اللي عامل الرص وتاج الرص/تدريب
    /// بيستخدموه (شوف ProductionFlowService)، وبيعلّم حاضر تلقائيًا
    /// كجزء منه.
    ///
    /// يوم عدّله المدير بإيده (سهر أو غياب فعلي، شوف
    /// <see cref="CorrectDayAsync"/> — بتتنادى من "بروفايل" الحساب في
    /// شاشة الحسابات الإدارية) بقى ليه سجل شغل بالساعة (أو اتشال عمدًا
    /// لو غياب)، فالتعبية دي **بتتجاهل اليوم ده تلقائيًا** بعد كده —
    /// بتملى الفاضي بس، مفيش استبدال لحاجة موجودة.
    /// </summary>
    public class DepartmentAttendanceService
    {
        private readonly IWorkerRepository _workerRepo;
        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly IAttendanceRepository _attendanceRepo;
        private readonly HourlyWorkdayService _hourlyWorkdayService;

        public DepartmentAttendanceService(
            IWorkerRepository workerRepo,
            IHourlyWorkLogRepository hourlyRepo,
            IAttendanceRepository attendanceRepo,
            HourlyWorkdayService hourlyWorkdayService)
        {
            _workerRepo = workerRepo;
            _hourlyRepo = hourlyRepo;
            _attendanceRepo = attendanceRepo;
            _hourlyWorkdayService = hourlyWorkdayService;
        }

        public async Task EnsureDailyPresenceAsync()
        {
            var accounts = (await _workerRepo.GetDepartmentAccountsAsync())
                .Where(w => w.IsActive)
                .ToList();

            var today = DateTime.Today;

            foreach (var account in accounts)
            {
                var from = account.CreatedAt.Date;
                if (from > today) continue; // احتياطًا: حساب اتضاف بتاريخ مستقبلي

                for (var day = from; day <= today; day = day.AddDays(1))
                {
                    if (await _hourlyRepo.GetByWorkerAndDateAsync(account.Id, day) is not null)
                        continue; // يوم اتسجل له شغل خلاص (تلقائي قبل كده أو سهر عدّله المستخدم) — سيبه زي ما هو

                    // غياب عدّله المدير بإيده (CorrectDayAsync) بيشيل سجل
                    // الشغل عمدًا وبيسيب سجل حضور بس — من غير الفحص ده
                    // التعبية كانت هترجع تحط يومية حاضر فوق غياب مسجّل
                    if (await _attendanceRepo.GetByWorkerAndDateAsync(account.Id, day) is not null)
                        continue;

                    await _hourlyWorkdayService.RecordHourlyWorkAsync(
                        account.Id, day, HourlyWorkdayService.ShiftEndHour);
                }
            }
        }

        /// <summary>
        /// تصحيح يدوي ليوم واحد لحساب إداري — من "بروفايل" الحساب في
        /// الشاشة. حاضر: نفس RecordHourlyWorkAsync (يومية شيفت عادي أو
        /// سهر). غياب: بيشيل سجل الشغل بالساعة لليوم ده لو موجود الأول
        /// (نفس قاعدة "الغياب مع شغل مسجّل ممنوع" في AttendanceService)
        /// وبعدين يعلّم الحضور بالحالة المطلوبة.
        /// </summary>
        public async Task CorrectDayAsync(int workerId, DateTime date, AttendanceStatus status, int endHour24)
        {
            if (status == AttendanceStatus.Present)
            {
                await _hourlyWorkdayService.RecordHourlyWorkAsync(workerId, date, endHour24);
                return;
            }

            var existingLog = await _hourlyRepo.GetByWorkerAndDateAsync(workerId, date.Date);
            if (existingLog is not null)
                _hourlyRepo.Remove(existingLog);

            var existingAttendance = await _attendanceRepo.GetByWorkerAndDateAsync(workerId, date.Date);
            if (existingAttendance is null)
                await _attendanceRepo.AddAsync(new Attendance
                {
                    WorkerId = workerId,
                    Date = date.Date,
                    Status = status
                });
            else
                existingAttendance.Status = status;

            if (existingAttendance is not null)
                _attendanceRepo.Update(existingAttendance);

            await _attendanceRepo.SaveChangesAsync();
        }
    }
}
