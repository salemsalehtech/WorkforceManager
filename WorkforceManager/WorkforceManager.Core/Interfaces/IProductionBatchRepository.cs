using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IProductionBatchRepository : IGenericRepository<ProductionBatch>
    {
        /// <summary>
        /// الدفعات الواقفة في الخط لمنتج معين، مع المرحلة اللي واقفة عندها.
        /// دي اللي بتتعرض للمستخدم عشان يختار منها اللي هيكمّلها.
        /// </summary>
        Task<IReadOnlyList<ProductionBatch>> GetOpenByProductAsync(int productId);

        /// <summary>كل الواقف في المصنع (كل المنتجات) — لشاشة إقفال اليوم والتقرير</summary>
        Task<IReadOnlyList<ProductionBatch>> GetAllOpenAsync();

        /// <summary>دفعة واحدة مع منتجها ومرحلتها الحالية</summary>
        Task<ProductionBatch?> GetWithDetailsAsync(int batchId);

        /// <summary>الدفعات اللي خلصت في يوم معين — أساس "المكتمل النهارده"</summary>
        Task<IReadOnlyList<ProductionBatch>> GetCompletedOnAsync(DateTime date);

        /// <summary>الدفعات اللي خلصت في فترة (للتقارير الأسبوعية/الشهرية)</summary>
        Task<IReadOnlyList<ProductionBatch>> GetCompletedBetweenAsync(DateTime from, DateTime to);

        /// <summary>
        /// الدفعات اللي كانت لسه مفتوحة بنهاية يوم معين — يعني بدأت في اليوم
        /// ده أو قبله ولسه ماخلصتش (أو خلصت في يوم بعده). دي "الواقف" في
        /// تقرير اليوم ده حتى لو بصّينا عليه بعد أسبوع.
        /// </summary>
        Task<IReadOnlyList<ProductionBatch>> GetOpenAsOfAsync(DateTime date);
    }
}
