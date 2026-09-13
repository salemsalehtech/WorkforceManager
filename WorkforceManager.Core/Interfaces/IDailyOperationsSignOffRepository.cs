using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IDailyOperationsSignOffRepository : IGenericRepository<DailyOperationsSignOff>
    {
        /// <summary>توقيع اليوم ده لو موجود (null = اليوم لسه ما اتوقّعش)</summary>
        Task<DailyOperationsSignOff?> GetByDateAsync(DateTime date);

        /// <summary>اليوم ده موقّع؟ — التحقق السريع قبل السماح بإغلاق البرنامج</summary>
        Task<bool> IsSignedOffAsync(DateTime date);

        /// <summary>
        /// أحدث يوم موقّع (null لو الجدول فاضي بالكامل — أول تشغيل بعد
        /// الـ Migration، قبل ما بذرة الـ cutover تتحط).
        /// </summary>
        Task<DateTime?> GetMostRecentDateAsync();

        /// <summary>تسجيل عدة أيام دفعة واحدة (لحاق بدء التشغيل) في نداء واحد</summary>
        Task AddRangeAsync(IEnumerable<DailyOperationsSignOff> signOffs);
    }
}
