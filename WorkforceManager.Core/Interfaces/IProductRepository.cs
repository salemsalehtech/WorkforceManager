using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IProductRepository : IGenericRepository<Product>
    {
        /// <summary>إحضار منتج مع كل مراحله وأسعارها</summary>
        Task<Product?> GetWithStagesAsync(int productId);

        /// <summary>كل المنتجات النشطة فقط مع مراحلها (للاستخدام في شاشة تسجيل الإنتاج)</summary>
        Task<IReadOnlyList<Product>> GetActiveWithStagesAsync();

        /// <summary>كل المنتجات (النشطة والموقوفة) مع كل مراحلها — لشاشة إدارة المنتجات</summary>
        Task<IReadOnlyList<Product>> GetAllWithStagesAsync();

        /// <summary>منتجات بعينها بمعرّفاتها — استعلام واحد للتعيين الجماعي (مش واحد لكل منتج)</summary>
        Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids);
    }
}
