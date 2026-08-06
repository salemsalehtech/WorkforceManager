using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class ProductionDayClosureRepository
        : GenericRepository<ProductionDayClosure>, IProductionDayClosureRepository
    {
        public ProductionDayClosureRepository(AppDbContext context) : base(context) { }

        public async Task<ProductionDayClosure?> GetByDateAsync(DateTime date) =>
            await DbSet.FirstOrDefaultAsync(c => c.Date == date.Date);

        public async Task<bool> IsClosedAsync(DateTime date) =>
            await DbSet.AnyAsync(c => c.Date == date.Date);
    }
}
