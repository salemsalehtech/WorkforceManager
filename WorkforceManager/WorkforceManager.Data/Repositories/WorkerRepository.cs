using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class WorkerRepository : GenericRepository<Worker>, IWorkerRepository
    {
        public WorkerRepository(AppDbContext context) : base(context) { }

        public async Task<Worker?> GetWithSkillsAsync(int workerId)
        {
            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .FirstOrDefaultAsync(w => w.Id == workerId);
        }

        public async Task<IReadOnlyList<Worker>> GetActiveWithSkillsAsync()
        {
            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .Where(w => w.IsActive)
                .OrderBy(w => w.FullName)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Worker>> GetAllWithSkillsAsync()
        {
            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .OrderBy(w => w.FullName)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<WorkerSkill>> GetSkillsForProductAsync(int productId)
        {
            // استعلام واحد بيرجع (المرحلة، العامل) لكل مراحل المنتج — بدل
            // استعلام منفصل لكل مرحلة في شاشة رحلة الإنتاج
            return await Context.Set<WorkerSkill>()
                .Include(ws => ws.Worker)
                .Where(ws => ws.ProductionStage.ProductId == productId && ws.Worker.IsActive)
                .OrderBy(ws => ws.Worker.FullName)
                .ToListAsync();
        }
    }
}
