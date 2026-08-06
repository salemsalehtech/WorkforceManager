using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IHourlyWorkLogRepository : IGenericRepository<HourlyWorkLog>
    {
        /// <summary>سجل شغل عامل بالساعة في يوم معين (لمنع التكرار — سجل واحد لكل عامل/يوم)</summary>
        Task<HourlyWorkLog?> GetByWorkerAndDateAsync(int workerId, DateTime date);

        /// <summary>كل سجلات الشغل بالساعة في يوم معين (لتبويب التسجيل اليومي)</summary>
        Task<IReadOnlyList<HourlyWorkLog>> GetByDateAsync(DateTime date);

        /// <summary>كل سجلات الشغل بالساعة خلال فترة زمنية (للملخص الأسبوعي والتقارير)</summary>
        Task<IReadOnlyList<HourlyWorkLog>> GetByRangeAsync(DateTime from, DateTime to);

        /// <summary>فيه أي شغل بالساعة مسجّل للعامل ده؟</summary>
        Task<bool> HasAnyForWorkerAsync(int workerId);
    }
}
