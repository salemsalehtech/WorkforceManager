using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// حساب يوميات العمال بالساعة وتسجيلها. القاعدة المتفق عليها
    /// (الشيفت الأساسي 8 صباحًا → 4 مساءً)، والحساب بيتحدد بوقت انتهاء
    /// الشغل (مش عدد ساعات مجرّد)، وغير تراكمي — بيتحدد بآخر فترة وصلها:
    ///
    /// - خلص لحد 4 مساءً (داخل الشيفت): بالنسبة — كل ساعة شغل = 1/8
    ///   يومية (4 ساعات = 0.5، 8 ساعات = 1.0 يومية).
    /// - خلص بين 4 مساءً و 8 مساءً (الفترة المسائية): 1.5 يومية.
    /// - خلص بين 8 مساءً و 12 منتصف الليل (الفترة الليلية): 2.0 يومية.
    ///
    /// اليوميات المحسوبة بتتخزن كـ Snapshot في السجل عشان تفضل ثابتة
    /// حتى لو القاعدة اتغيرت بعدين.
    /// </summary>
    public class HourlyWorkdayService
    {
        /// <summary>بداية الشيفت الثابتة (8 صباحًا) بنظام 24 ساعة</summary>
        public const int ShiftStartHour = 8;

        /// <summary>نهاية الشيفت الأساسي (4 مساءً) — بعده يبدأ الأوفرتايم</summary>
        public const int ShiftEndHour = 16;

        /// <summary>نهاية الفترة المسائية (8 مساءً)</summary>
        public const int EveningEndHour = 20;

        /// <summary>نهاية الفترة الليلية (12 منتصف الليل)</summary>
        public const int NightEndHour = 24;

        private const decimal EveningWorkdays = 1.5m;
        private const decimal NightWorkdays = 2.0m;

        private readonly IHourlyWorkLogRepository _hourlyRepo;
        private readonly IAttendanceRepository _attendanceRepo;

        public HourlyWorkdayService(
            IHourlyWorkLogRepository hourlyRepo,
            IAttendanceRepository attendanceRepo)
        {
            _hourlyRepo = hourlyRepo;
            _attendanceRepo = attendanceRepo;
        }

        /// <summary>
        /// اختصارات الشيفت الجاهزة المعروضة في شاشة الحضور.
        ///
        /// السلّم تحت مبيطلّعش غير تلات نتايج مختلفة مهما كان وقت
        /// الانتهاء بالظبط (1 / 1.5 / 2 يومية)، فبدل ما المستخدم يفتح
        /// قائمة بـ13 ساعة ويدوّر، بيدوس على واحد من التلاتة على طول.
        /// اللي عايز وقت انتهاء مخصوص (مثلاً خلص 11 صباحًا = نص يومية)
        /// لسه ينفع يتسجل من <see cref="RecordHourlyWorkAsync"/> مباشرة.
        /// </summary>
        public static IReadOnlyList<(int EndHour24, string Label)> ShiftPresets { get; } = new[]
        {
            (ShiftEndHour, "شيفت عادي"),   // 4 مساءً  → 1 يومية
            (EveningEndHour, "لحد 8 م"),    // 8 مساءً  → 1.5 يومية
            (NightEndHour, "لحد 12")        // 12 منتصف الليل → 2 يومية
        };

        /// <summary>
        /// يحسب عدد اليوميات من وقت انتهاء الشغل (بنظام 24 ساعة، الشيفت
        /// بيبدأ 8 صباحًا). دالة نقية (بدون قاعدة بيانات) عشان تكون سهلة
        /// الاختبار والمعاينة اللحظية في الواجهة.
        /// </summary>
        public static decimal ComputeWorkdays(int endHour24)
        {
            if (endHour24 <= ShiftStartHour)
                return 0m; // خلص أول ما بدأ (أو قبل) — مفيش شغل

            if (endHour24 <= ShiftEndHour)
            {
                // داخل الشيفت الأساسي: بالنسبة لعدد ساعات الشغل الفعلية
                var hoursWorked = endHour24 - ShiftStartHour;
                return Math.Round((decimal)hoursWorked / (ShiftEndHour - ShiftStartHour), 2);
            }

            if (endHour24 <= EveningEndHour)
                return EveningWorkdays; // خلص في الفترة المسائية

            // خلص في الفترة الليلية (لحد منتصف الليل) — وأي وقت بعدها بيتعامل زيها
            return NightWorkdays;
        }

        /// <summary>
        /// يسجل شغل عامل بالساعة في يوم معين (Upsert — سجل واحد لكل عامل/يوم).
        /// بيحسب اليوميات ويخزنها Snapshot، وبيسجل حضور "حاضر" تلقائيًا
        /// لو مالوش سجل حضور في اليوم (نفس منطق رحلة الإنتاج).
        /// </summary>
        public async Task<HourlyWorkLog> RecordHourlyWorkAsync(
            int workerId, DateTime date, int endHour24, string? notes = null)
        {
            if (endHour24 < ShiftStartHour + 1 || endHour24 > NightEndHour)
                throw new ArgumentException(
                    "وقت الانتهاء لازم يكون بعد بداية الشيفت (8 صباحًا) ولحد 12 منتصف الليل", nameof(endHour24));

            var workdays = ComputeWorkdays(endHour24);

            var existing = await _hourlyRepo.GetByWorkerAndDateAsync(workerId, date);
            HourlyWorkLog record;
            if (existing is not null)
            {
                existing.EndHour24 = endHour24;
                existing.WorkdaysCredited = workdays;
                existing.Notes = notes;
                _hourlyRepo.Update(existing);
                record = existing;
            }
            else
            {
                record = new HourlyWorkLog
                {
                    WorkerId = workerId,
                    Date = date.Date,
                    EndHour24 = endHour24,
                    WorkdaysCredited = workdays,
                    Notes = notes
                };
                await _hourlyRepo.AddAsync(record);
            }

            await _hourlyRepo.SaveChangesAsync();

            // حضور تلقائي لو مالوش سجل في اليوم (من غير ما يلمس أي سجل موجود)
            var attendance = await _attendanceRepo.GetByWorkerAndDateAsync(workerId, date);
            if (attendance is null)
            {
                await _attendanceRepo.AddAsync(new Attendance
                {
                    WorkerId = workerId,
                    Date = date.Date,
                    Status = Core.Enums.AttendanceStatus.Present
                });
                await _attendanceRepo.SaveChangesAsync();
            }

            return record;
        }

        /// <summary>يحذف سجل شغل بالساعة (تصحيح تسجيل غلط — حذف فعلي زي باقي التصحيحات)</summary>
        public async Task DeleteHourlyWorkAsync(int recordId)
        {
            var record = await _hourlyRepo.GetByIdAsync(recordId)
                ?? throw new InvalidOperationException("سجل الشغل بالساعة غير موجود");

            _hourlyRepo.Remove(record);
            await _hourlyRepo.SaveChangesAsync();
        }

        /// <summary>كل سجلات الشغل بالساعة في يوم معين (لتبويب التسجيل)</summary>
        public Task<IReadOnlyList<HourlyWorkLog>> GetByDateAsync(DateTime date)
            => _hourlyRepo.GetByDateAsync(date);
    }
}
