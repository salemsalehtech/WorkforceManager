using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IProductionMemoryRepository : IGenericRepository<ProductionMemory>
    {
        /// <summary>
        /// خطة واحدة بمراحلها ومنتجها. **المنتج بيترجّع حتى لو متشال
        /// أو موقوف** — الشاشة محتاجة تعرض التذكير وتوضّح إنه بايت،
        /// مش تخفيه في صمت.
        /// </summary>
        Task<ProductionMemory?> GetWithStagesAsync(int id);

        /// <summary>الخطط النشطة (لسه ما اتبدأتش)، الأقرب تذكيرًا الأول</summary>
        Task<IReadOnlyList<ProductionMemory>> GetActiveAsync();

        /// <summary>الخطط المنجزة، الأحدث الأول (قايمة للمرجع بس)</summary>
        Task<IReadOnlyList<ProductionMemory>> GetCompletedAsync();

        /// <summary>
        /// الخطط اللي تذكيرها حان: نشطة و<c>RemindOn &lt;= today</c>.
        /// المتأخر داخل عن قصد — التذكير بيضرب أول تشغيل في اليوم ده أو
        /// بعده. مرتّبة بالأقدم عشان تتعرض بترتيب حصولها.
        /// </summary>
        Task<IReadOnlyList<ProductionMemory>> GetDueAsync(DateTime today);
    }
}
