using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    /// <summary>
    /// استعلامات العمال.
    ///
    /// كل دالة بترجّع قايمة للعرض أو الاختيار بتنادي
    /// <see cref="SoftDeleteQueryExtensions.ExcludeDeleted{T}"/> — العامل
    /// المحذوف ميظهرش في أي قايمة، ولا حتى في فلتر "الموقوفين".
    ///
    /// السبب مهم: الحذف بيعلّم العامل موقوف كمان (عشان يختفي من قوايم
    /// الاختيار)، فمن غير الاستثناء ده كان بيقع في فلتر الموقوفين ويرجع
    /// بزرار "إعادة تفعيل" — يعني الحذف مالوش أي معنى.
    ///
    /// الاستعلامات التاريخية (تقارير، أجور) **مبتستثنيش** عن قصد، عشان
    /// السجل القديم يفضل يعرض اسم صاحبه.
    /// </summary>
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
                .ExcludeDeleted()
                .Include(w => w.Skills)
                    .ThenInclude(s => s.ProductionStage)
                        .ThenInclude(ps => ps.Product)
                .Where(w => w.IsActive)
                .OrderBy(w => w.FullName)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Worker>> GetAllWithSkillsAsync()
        {
            // "الكل" هنا معناها كل العمال الموجودين — الموقوف منهم والنشط.
            // المحذوف مش "موقوف"، هو مبقاش من المصنع خالص
            return await DbSet
                .ExcludeDeleted()
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
            // العامل المحذوف بيتفلتر بالفلتر العام على WorkerSkill نفسه
            return await Context.Set<WorkerSkill>()
                .Include(ws => ws.Worker)
                .Where(ws => ws.ProductionStage.ProductId == productId && ws.Worker.IsActive)
                .OrderBy(ws => ws.Worker.FullName)
                .ToListAsync();
        }
    }
}
