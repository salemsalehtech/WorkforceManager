using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    public class WorkerSkillRepository : GenericRepository<WorkerSkill>, IWorkerSkillRepository
    {
        public WorkerSkillRepository(AppDbContext context) : base(context) { }

        public Task<WorkerSkill?> GetAsync(int workerId, int stageId) =>
            DbSet.FirstOrDefaultAsync(ws =>
                ws.WorkerId == workerId && ws.ProductionStageId == stageId);

        public async Task<IReadOnlyList<WorkerSkill>> GetByWorkerAsync(int workerId)
        {
            return await DbSet
                .Include(ws => ws.ProductionStage)
                    .ThenInclude(s => s.Product)
                .Where(ws => ws.WorkerId == workerId)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<WorkerSkill>> GetByStageAsync(int stageId)
        {
            return await DbSet
                .Include(ws => ws.Worker)
                .Where(ws => ws.ProductionStageId == stageId)
                // العامل الموقوف مش بيظهر في قوايم الاختيار — الحذف الناعم
                // بيتفلتر لوحده بالفلتر العام على WorkerSkill
                .Where(ws => ws.Worker.IsActive)
                .ToListAsync();
        }
    }
}
