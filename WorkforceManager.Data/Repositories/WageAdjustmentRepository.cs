using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class WageAdjustmentRepository : GenericRepository<WageAdjustment>, IWageAdjustmentRepository
    {
        public WageAdjustmentRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<WageAdjustment>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to)
        {
            return await DbSet
                .Where(a => a.WorkerId == workerId && a.Date >= from.Date && a.Date <= to.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<WageAdjustment>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Include(a => a.Worker) // اسم العامل مطلوب في كشف الأجور المجمّع
                .Where(a => a.Date >= from.Date && a.Date <= to.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<WageAdjustment>> GetByDateAsync(DateTime date)
        {
            return await DbSet
                .Include(a => a.Worker) // اسم العامل مطلوب في قائمة عرض اليوم
                .Where(a => a.Date == date.Date)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
        }

        public Task<bool> HasAnyForWorkerAsync(int workerId) =>
            DbSet.AnyAsync(a => a.WorkerId == workerId);
    }
}
