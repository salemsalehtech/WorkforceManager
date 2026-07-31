using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IProductionDayClosureRepository : IGenericRepository<ProductionDayClosure>
    {
        /// <summary>إقفال اليوم ده لو موجود (null = اليوم لسه مفتوح)</summary>
        Task<ProductionDayClosure?> GetByDateAsync(DateTime date);

        /// <summary>اليوم ده مقفول؟ — التحقق السريع قبل أي تسجيل إنتاج</summary>
        Task<bool> IsClosedAsync(DateTime date);
    }
}
