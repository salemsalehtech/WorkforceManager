using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class ProductionBatchRepository : GenericRepository<ProductionBatch>, IProductionBatchRepository
    {
        public ProductionBatchRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<ProductionBatch>> GetOpenByProductAsync(int productId)
        {
            return await OpenWithDetails()
                .Where(b => b.ProductId == productId)
                .OrderBy(b => b.StartedDate).ThenBy(b => b.Id)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductionBatch>> GetAllOpenAsync()
        {
            return await OpenWithDetails()
                .OrderBy(b => b.Product.Name).ThenBy(b => b.StartedDate).ThenBy(b => b.Id)
                .ToListAsync();
        }

        public async Task<ProductionBatch?> GetWithDetailsAsync(int batchId)
        {
            return await DbSet
                .Include(b => b.Product)
                .Include(b => b.LastCompletedStage)
                .FirstOrDefaultAsync(b => b.Id == batchId);
        }

        public async Task<IReadOnlyList<ProductionBatch>> GetCompletedOnAsync(DateTime date)
        {
            return await CompletedWithDetails()
                .Where(b => b.CompletedDate!.Value.Date == date.Date)
                .OrderBy(b => b.Product.Name).ThenBy(b => b.Id)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductionBatch>> GetCompletedBetweenAsync(DateTime from, DateTime to)
        {
            return await CompletedWithDetails()
                .Where(b => b.CompletedDate!.Value.Date >= from.Date && b.CompletedDate!.Value.Date <= to.Date)
                .OrderBy(b => b.CompletedDate).ThenBy(b => b.Product.Name)
                .ToListAsync();
        }

        /// <summary>
        /// "كان واقف بنهاية اليوم ده" = بدأ في اليوم ده أو قبله، ولسه مفتوح
        /// أو خلص في يوم بعده. الشرط التاني هو اللي بيخلي تقرير يوم قديم
        /// يفضل صحيح لما تفتحه بعد أسبوع — مش بيقول "مفيش واقف" لمجرد إن
        /// الدفعة خلصت بعدين.
        /// </summary>
        public async Task<IReadOnlyList<ProductionBatch>> GetOpenAsOfAsync(DateTime date)
        {
            var day = date.Date;

            return await DbSet
                .Include(b => b.Product)
                .Include(b => b.LastCompletedStage)
                .Where(b => b.StartedDate.Date <= day
                            && b.Status != BatchStatus.Cancelled
                            && (b.CompletedDate == null || b.CompletedDate.Value.Date > day))
                .OrderBy(b => b.Product.Name).ThenBy(b => b.StartedDate).ThenBy(b => b.Id)
                .ToListAsync();
        }

        private IQueryable<ProductionBatch> OpenWithDetails() =>
            DbSet.Include(b => b.Product)
                 .Include(b => b.LastCompletedStage)
                 .Where(b => b.Status == BatchStatus.Open);

        private IQueryable<ProductionBatch> CompletedWithDetails() =>
            DbSet.Include(b => b.Product)
                 .Where(b => b.Status == BatchStatus.Completed && b.CompletedDate != null);
    }
}
