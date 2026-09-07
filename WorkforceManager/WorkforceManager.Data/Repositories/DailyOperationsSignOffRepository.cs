using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class DailyOperationsSignOffRepository
        : GenericRepository<DailyOperationsSignOff>, IDailyOperationsSignOffRepository
    {
        public DailyOperationsSignOffRepository(AppDbContext context) : base(context) { }

        public async Task<DailyOperationsSignOff?> GetByDateAsync(DateTime date) =>
            await DbSet.FirstOrDefaultAsync(s => s.Date == date.Date);

        public async Task<bool> IsSignedOffAsync(DateTime date) =>
            await DbSet.AnyAsync(s => s.Date == date.Date);

        public async Task<DateTime?> GetMostRecentDateAsync() =>
            await DbSet.OrderByDescending(s => s.Date).Select(s => (DateTime?)s.Date).FirstOrDefaultAsync();

        public async Task AddRangeAsync(IEnumerable<DailyOperationsSignOff> signOffs) =>
            await DbSet.AddRangeAsync(signOffs);
    }
}
