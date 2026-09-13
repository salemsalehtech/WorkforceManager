using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class ProductionMemoryRepository
        : GenericRepository<ProductionMemory>, IProductionMemoryRepository
    {
        public ProductionMemoryRepository(AppDbContext context) : base(context) { }

        /// <summary>
        /// المراحل بترتيب Position، والمنتج بمراحله الحية.
        ///
        /// المنتج **مش** مفلتر بالحذف/الإيقاف هنا عن قصد: الخطة اللي
        /// منتجها بات لازم تفضل تظهر في القايمة والتذكير برسالة واضحة،
        /// والشاشة هي اللي بتقرر تعطّل "ابدأ الآن".
        /// </summary>
        private IQueryable<ProductionMemory> WithDetails() =>
            DbSet
                .Include(m => m.Product)
                    .ThenInclude(p => p.Stages.Where(s => !s.IsDeleted))
                .Include(m => m.Stages.OrderBy(s => s.Position))
                    .ThenInclude(ms => ms.ProductionStage);

        public async Task<ProductionMemory?> GetWithStagesAsync(int id) =>
            await WithDetails().FirstOrDefaultAsync(m => m.Id == id);

        public async Task<IReadOnlyList<ProductionMemory>> GetActiveAsync() =>
            await WithDetails()
                .Where(m => m.CompletedAt == null)
                .OrderBy(m => m.RemindOn).ThenBy(m => m.Id)
                .ToListAsync();

        public async Task<IReadOnlyList<ProductionMemory>> GetCompletedAsync() =>
            await WithDetails()
                .Where(m => m.CompletedAt != null)
                .OrderByDescending(m => m.CompletedAt).ThenByDescending(m => m.Id)
                .ToListAsync();

        public async Task<IReadOnlyList<ProductionMemory>> GetDueAsync(DateTime today)
        {
            var cutoff = today.Date;

            return await WithDetails()
                .Where(m => m.CompletedAt == null && m.RemindOn <= cutoff)
                .OrderBy(m => m.RemindOn).ThenBy(m => m.Id)
                .ToListAsync();
        }
    }
}
