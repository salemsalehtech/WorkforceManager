using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class AttendanceRepository : GenericRepository<Attendance>, IAttendanceRepository
    {
        public AttendanceRepository(AppDbContext context) : base(context) { }

        public async Task<Attendance?> GetByWorkerAndDateAsync(int workerId, DateTime date)
        {
            return await DbSet.FirstOrDefaultAsync(a => a.WorkerId == workerId && a.Date == date.Date);
        }

        public async Task<IReadOnlyList<Attendance>> GetByWorkerAndRangeAsync(int workerId, DateTime from, DateTime to)
        {
            return await DbSet
                .Where(a => a.WorkerId == workerId && a.Date >= from.Date && a.Date <= to.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Attendance>> GetByDateAsync(DateTime date)
        {
            return await DbSet
                .Include(a => a.Worker)
                .Where(a => a.Date == date.Date)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Attendance>> GetByRangeAsync(DateTime from, DateTime to)
        {
            return await DbSet
                .Include(a => a.Worker) // اسم العامل مطلوب في تجميع الملخص الأسبوعي
                .Where(a => a.Date >= from.Date && a.Date <= to.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }
    }
}
