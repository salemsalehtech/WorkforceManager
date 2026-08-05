using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    /// <summary>
    /// مهارات العمال. ريبو مخصص مش IGenericRepository عشان استعلامات
    /// التقييم محتاجة تحمّل العامل والمرحلة معاها — والتحميل ده لو اتساب
    /// للي بينادي بيتنسى وبيطلّع NullReference وقت العرض.
    /// </summary>
    public interface IWorkerSkillRepository : IGenericRepository<WorkerSkill>
    {
        /// <summary>مهارة عامل واحد على مرحلة واحدة (null لو مش مربوط بيها)</summary>
        Task<WorkerSkill?> GetAsync(int workerId, int stageId);

        /// <summary>كل مهارات عامل، مع المرحلة ومنتجها (لبروفايل العامل)</summary>
        Task<IReadOnlyList<WorkerSkill>> GetByWorkerAsync(int workerId);

        /// <summary>كل العمال المؤهلين لمرحلة، مع بيانات العامل (لقوايم الاختيار)</summary>
        Task<IReadOnlyList<WorkerSkill>> GetByStageAsync(int stageId);

        /// <summary>
        /// كل المهارات اللي ليها قياس أداء، بالعامل والمرحلة والمنتج —
        /// أساس المراجعة الشهرية. العمال الموقوفين مستثنيين: مفيش داعي
        /// المدير يراجع تقييم حد مش شغّال أصلاً.
        /// </summary>
        Task<IReadOnlyList<WorkerSkill>> GetAllMeasuredAsync();
    }
}
