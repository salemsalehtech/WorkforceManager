using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class HourlyWorkLogRepository : GenericRepository<HourlyWorkLog>, IHourlyWorkLogRepository
    {
        public HourlyWorkLogRepository(AppDbContext context) : base(context) { }

        public async Task<HourlyWorkLog?> GetByWorkerAndDateAsync(int workerId, DateTime date)
        {
            return await DbSet.FirstOrDefaultAsync(h => h.WorkerId == workerId && h.Date == date.Date);
        }

        public async Task<IReadOnlyList<HourlyWorkLog>> GetByDateAsync(DateTime date)
        {
            return await DbSet
                .Include(h => h.Worker)
                .Where(h => h.Date == date.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<HourlyWorkLog>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Include(h => h.Worker) // اسم العامل مطلوب في تجميع الملخص الأسبوعي
                .Where(h => h.Date >= from.Date && h.Date <= to.Date)
                .OrderBy(h => h.Date)
                .ToListAsync();
        }
    }
}
