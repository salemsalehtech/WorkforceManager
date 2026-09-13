using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IAttendanceRepository : IGenericRepository<Attendance>
    {
        /// <summary>سجل حضور عامل معين في تاريخ معين (لمنع التسجيل المكرر)</summary>
        Task<Attendance?> GetByWorkerAndDateAsync(int workerId, DateTime date);

        /// <summary>كل سجلات حضور عامل معين خلال فترة زمنية (لعرضها في ملفه وفي التقييم)</summary>
        Task<IReadOnlyList<Attendance>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to);

        /// <summary>كل سجلات الحضور في تاريخ معين (لتقرير الحضور اليومي لكل القسم)</summary>
        Task<IReadOnlyList<Attendance>> GetByDateAsync(DateTime date);

        /// <summary>كل سجلات الحضور لكل العمال خلال فترة زمنية (للملخص والتقرير الأسبوعي المجمّع)</summary>
        Task<IReadOnlyList<Attendance>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>أحدث تاريخ ليه سجل حضور لعامل معيّن — null لو مفيش أي سجل خالص</summary>
        Task<DateTime?> GetLastDateForWorkerAsync(int workerId);
    }
}
