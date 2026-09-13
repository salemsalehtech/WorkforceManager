using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;
using WorkforceManager.Data;

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
        private readonly ActivityLogService _log;

        public DepartmentAttendanceService(
            IWorkerRepository workerRepo,
            IHourlyWorkLogRepository hourlyRepo,
            IAttendanceRepository attendanceRepo,
            HourlyWorkdayService hourlyWorkdayService,
            ActivityLogService log)
        {
            _workerRepo = workerRepo;
            _hourlyRepo = hourlyRepo;
            _attendanceRepo = attendanceRepo;
            _hourlyWorkdayService = hourlyWorkdayService;
            _log = log;
        }

        public async Task EnsureDailyPresenceAsync()
        {
            var accounts = (await _workerRepo.GetDepartmentAccountsAsync())
                .Where(w => w.IsActive)
                .ToList();

            var today = DateTime.Today;

            foreach (var account in accounts)
            {
                // **ده الإصلاح**: كانت الحلقة بتبدأ من تاريخ إنشاء الحساب
                // في كل بدء تشغيل، وبتعمل فحص يوم بيوم (2 استعلام لكل
                // يوم) لحد النهارده — يعني حساب عمره سنة بيعمل 700+
                // استعلام متتابع كل مرة البرنامج يتفتح، والرقم ده بيكبر
                // للأبد مع عمر الحساب. دلوقتي بنسأل مصدر البيانات نفسه
                // "آخر يوم ليك فيه سجل؟" (استعلام واحد لكل مصدر، بيستفيد
                // من الفهرس اليونيك (WorkerId, Date) الموجود أصلًا) بدل
                // ما نعيد فحص كل يوم قبله من الصفر — فالحلقة اللي تحت
                // بترجع لحجمها الطبيعي: يوم أو اتنين بس (الفرق من آخر
                // تشغيل)، مهما كان عمر الحساب. **متعمدين عدم استخدام
                // مؤشر مركزي محفوظ**: مؤشر واحد لكل الحسابات كان بيفشل
                // لو حساب جديد اتضاف بعد ما المؤشر يتقدّم — استعلام لكل
                // حساب دايمًا صحيح بغض النظر عن ترتيب إضافة الحسابات.
                var lastHourly = await _hourlyRepo.GetLastDateForWorkerAsync(account.Id);
                var lastAttendance = await _attendanceRepo.GetLastDateForWorkerAsync(account.Id);
                var startDay = new[] { account.CreatedAt.Date, lastHourly?.AddDays(1), lastAttendance?.AddDays(1) }
                    .Where(d => d.HasValue)
                    .Max()!.Value;

                for (var day = startDay; day <= today; day = day.AddDays(1))
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
        ///
        /// **بيتسجّل في سجل العمليات دايمًا** — كانت دي فجوة حقيقية: التصحيح
        /// ده بيغيّر يومية مدفوعة (لحد يومية ونص) بلا أي أثر خالص، وأخطر
        /// من كده، بلا ما "يفكّ" توقيع نهاية اليوم لو اليوم ده كان موقّع
        /// بالفعل — IsFullySignedOffAsync بيقرا سجل العمليات بس عشان
        /// يعرف "حصل حاجة بعد التوقيع ولا لأ"، فتعديل من غير أي حدث كان
        /// بيمر من غير ما يفكّ التوقيع، عكس أي تعديل حضور/أجر تاني في
        /// البرنامج بالظبط.
        /// </summary>
        public async Task CorrectDayAsync(int workerId, DateTime date, AttendanceStatus status, int endHour24)
        {
            var account = await _workerRepo.GetByIdAsync(workerId);
            var details = status == AttendanceStatus.Present
                ? $"حاضر — حتى الساعة {endHour24}:00"
                : status == AttendanceStatus.AbsentWithPermission
                    ? "غياب بإذن"
                    : "غياب بدون إذن";

            if (status == AttendanceStatus.Present)
            {
                await _hourlyWorkdayService.RecordHourlyWorkAsync(workerId, date, endHour24);
                await _log.LogAsync(
                    ActivityEventType.DepartmentAccountDayCorrected, "Worker", workerId,
                    entityName: account?.FullName, details: $"يوم {date:yyyy/MM/dd} — {details}");
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

            await _log.LogAsync(
                ActivityEventType.DepartmentAccountDayCorrected, "Worker", workerId,
                entityName: account?.FullName, details: $"يوم {date:yyyy/MM/dd} — {details}");
        }
    }
}
