using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Data.Repositories
{
    /// <summary>
    /// استعلامات المنتجات ومراحلها.
    ///
    /// نفس قاعدة العمال: كل قايمة للعرض بتستثني المحذوف — المنتج
    /// المحذوف مش "موقوف"، هو مبقاش من المصنع، فمينفعش يظهر في فلتر
    /// الموقوفين ويرجع بزرار "إعادة تفعيل".
    /// والمراحل المحذوفة بتتشال من كل منتج بنفس المنطق.
    /// </summary>
    public class ProductRepository : GenericRepository<Product>, IProductRepository
    {
        public ProductRepository(AppDbContext context) : base(context) { }

        public async Task<Product?> GetWithStagesAsync(int productId)
        {
            return await DbSet
                .Include(p => p.Stages.Where(s => !s.IsDeleted))
                .FirstOrDefaultAsync(p => p.Id == productId);
        }

        public async Task<IReadOnlyList<Product>> GetActiveWithStagesAsync()
        {
            return await DbSet
                .ExcludeDeleted()
                .Include(p => p.Stages.Where(s => s.IsActive && !s.IsDeleted))
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Product>> GetAllWithStagesAsync()
        {
            // شاشة الإدارة محتاجة كل حاجة بما فيها الموقوف (بيظهر بعلامة
            // مميزة) — لكن مش المحذوف
            return await DbSet
                .ExcludeDeleted()
                .Include(p => p.Stages.Where(s => !s.IsDeleted))
                .OrderBy(p => p.Name)
                .ToListAsync();
        }
    }
}
