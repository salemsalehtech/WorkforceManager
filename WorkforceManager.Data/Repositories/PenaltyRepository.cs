using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class PenaltyRepository : GenericRepository<Penalty>, IPenaltyRepository
    {
        public PenaltyRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<Penalty>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to)
        {
            return await DbSet
                .Where(p => p.WorkerId == workerId && p.Date >= from.Date && p.Date <= to.Date)
                .OrderBy(p => p.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Penalty>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Include(p => p.Worker) // اسم العامل مطلوب في التقرير الأسبوعي المجمّع
                .Where(p => p.Date >= from.Date && p.Date <= to.Date)
                .OrderBy(p => p.Date)
                .ToListAsync();
        }

        public Task<bool> HasAnyForWorkerAsync(int workerId) =>
            DbSet.AnyAsync(p => p.WorkerId == workerId);
    }
}
