using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class WorkerRepository : GenericRepository<Worker>, IWorkerRepository
    {
        public WorkerRepository(AppDbContext context) : base(context) { }

        public async Task<IReadOnlyList<Worker>> SearchByNameAsync(string nameQuery)
        {
            var query = nameQuery.Trim();

            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .Where(w => w.IsActive && EF.Functions.Like(w.FullName, $"%{query}%"))
                .OrderBy(w => w.FullName)
                .ToListAsync();
        }

        public async Task<Worker?> GetWithSkillsAsync(int workerId)
        {
            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .FirstOrDefaultAsync(w => w.Id == workerId);
        }

        public async Task<IReadOnlyList<Worker>> SearchBySkillAsync(string skillQuery)
        {
            var query = skillQuery.Trim();

            // البحث بيشمل اسم المرحلة أو اسم المنتج أو ملاحظات المهارات النصية
            // (SkillsNotes) — لأن الربط الدقيق بالمراحل لسه بيتبني بالتدريج،
            // والملاحظات النصية هي المرجع الحالي لمهارات معظم العمال
            return await DbSet
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .Where(w => w.IsActive && (
                    w.Skills.Any(s =>
                        EF.Functions.Like(s.ProductionStage.StageName, $"%{query}%") ||
                        EF.Functions.Like(s.ProductionStage.Product.Name, $"%{query}%")) ||
                    (w.SkillsNotes != null && EF.Functions.Like(w.SkillsNotes, $"%{query}%"))))
                .OrderBy(w => w.FullName)
                .ToListAsync();
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

        public async Task<IReadOnlyList<Worker>> GetQualifiedForStageAsync(int productionStageId)
        {
            return await DbSet
                .Where(w => w.IsActive && w.Skills.Any(s => s.ProductionStageId == productionStageId))
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
