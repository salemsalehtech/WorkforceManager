using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    public interface IWorkerRepository : IGenericRepository<Worker>
    {
        /// <summary>إحضار عامل واحد مع كل مهاراته المرتبطة (المراحل التي يجيدها)</summary>
        Task<Worker?> GetWithSkillsAsync(int workerId);

        /// <summary>كل العمال النشطين بمهاراتهم — أساس شاشة العمال الرئيسية</summary>
        Task<IReadOnlyList<Worker>> GetActiveWithSkillsAsync();

        /// <summary>
        /// كل العمال (النشطين والموقوفين) بمهاراتهم. شاشة العمال بتحمّلهم
        /// مرة واحدة وبتفلتر وترتّب في الذاكرة، عشان البحث والفلاتر يبقوا
        /// لحظيين من غير رحلة لقاعدة البيانات مع كل حرف بيتكتب.
        /// </summary>
        Task<IReadOnlyList<Worker>> GetAllWithSkillsAsync();

        /// <summary>
        /// الحسابات الإدارية (مدير/رئيس قسم) بس — قايمة وشاشة منفصلة
        /// تمامًا عن "العمال والمهارات". <see cref="GetActiveWithSkillsAsync"/>
        /// و<see cref="GetAllWithSkillsAsync"/> يستثنوهم دايمًا، فهما مش
        /// بيظهروا في شاشة العمال ولا التقارير ولا رحلة الإنتاج.
        /// </summary>
        Task<IReadOnlyList<Worker>> GetDepartmentAccountsAsync();

        /// <summary>
        /// كل روابط المهارات (عامل ↔ مرحلة) لمراحل منتج معين، مع بيانات العامل،
        /// للعمال النشطين فقط — استعلام واحد بيجيب المؤهلين لكل مراحل المنتج
        /// دفعة واحدة (أساس شاشة رحلة الإنتاج: قائمة اختيار مستقلة لكل مرحلة).
        /// </summary>
        Task<IReadOnlyList<WorkerSkill>> GetSkillsForProductAsync(int productId);
    }
}
