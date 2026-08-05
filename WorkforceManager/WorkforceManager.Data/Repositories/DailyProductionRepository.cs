using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class DailyProductionRepository : GenericRepository<DailyProduction>, IDailyProductionRepository
    {
        public DailyProductionRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<DailyProduction>> GetByDateAsync(DateTime date)
        {
            return await DbSet
                .Include(dp => dp.Worker)
                .Include(dp => dp.ProductionStage)
                    .ThenInclude(ps => ps.Product)
                .Where(dp => dp.Date.Date == date.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<DailyProduction>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to)
        {
            return await DbSet
                .Include(dp => dp.ProductionStage)
                    .ThenInclude(ps => ps.Product)
                .Where(dp => dp.WorkerId == workerId && dp.Date.Date >= from.Date && dp.Date.Date <= to.Date)
                .OrderBy(dp => dp.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<DailyProduction>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Include(dp => dp.Worker) // اسم العامل مطلوب في تجميع الملخص الأسبوعي
                .Include(dp => dp.ProductionStage)
                    .ThenInclude(ps => ps.Product)
                .Where(dp => dp.Date.Date >= from.Date && dp.Date.Date <= to.Date)
                .OrderBy(dp => dp.Date)
                .ToListAsync();
        }

        /// <summary>التجميع بيتنفذ كـ GROUP BY في الداتابيز مش في الذاكرة</summary>
        public async Task<IReadOnlyDictionary<int, int>> GetStageTotalsOnAsync(DateTime date)
        {
            var totals = await DbSet
                .Where(dp => dp.Date.Date == date.Date)
                .GroupBy(dp => dp.ProductionStageId)
                .Select(g => new { StageId = g.Key, Pieces = g.Sum(dp => dp.PieceCount) })
                .ToListAsync();

            return totals.ToDictionary(t => t.StageId, t => t.Pieces);
        }
    }
}
