using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IWorkerRepository : IGenericRepository<Worker>
    {
        /// <summary>بحث بالاسم (جزء من الاسم) لإحضار العمال المطابقين مع مهاراتهم محمّلة</summary>
        Task<IReadOnlyList<Worker>> SearchByNameAsync(string nameQuery);

        /// <summary>إحضار عامل واحد مع كل مهاراته المرتبطة (المراحل التي يجيدها)</summary>
        Task<Worker?> GetWithSkillsAsync(int workerId);

        /// <summary>
        /// بحث بالمهارة/المرحلة: يرجع العمال النشطين اللي عندهم مهارة اسم
        /// مرحلتها أو اسم منتجها بيطابق الكلمة المكتوبة (مثال: "دبله" أو "GRS").
        /// أساس ميزة "اكتب اسم مرحلة، شوف مين بيعرف يعملها".
        /// </summary>
        Task<IReadOnlyList<Worker>> SearchBySkillAsync(string skillQuery);

        /// <summary>كل العمال النشطين بمهاراتهم — أساس شاشة العمال الرئيسية</summary>
        Task<IReadOnlyList<Worker>> GetActiveWithSkillsAsync();

        /// <summary>العمال النشطون المؤهلون لمرحلة معينة (أساس شاشة الإدخال السريع لليوميات)</summary>
        Task<IReadOnlyList<Worker>> GetQualifiedForStageAsync(int productionStageId);

        /// <summary>
        /// كل روابط المهارات (عامل ↔ مرحلة) لمراحل منتج معين، مع بيانات العامل،
        /// للعمال النشطين فقط — استعلام واحد بيجيب المؤهلين لكل مراحل المنتج
        /// دفعة واحدة (أساس شاشة رحلة الإنتاج: قائمة اختيار مستقلة لكل مرحلة).
        /// </summary>
        Task<IReadOnlyList<WorkerSkill>> GetSkillsForProductAsync(int productId);
    }
}
