using System.Linq;

namespace WorkforceManager.Core.Interfaces
{
    /// <summary>
    /// استثناء المحذوف من القوايم — المكان الوحيد اللي بيكتب الشرط ده
    /// للكيانات اللي مالهاش فلتر عام (العامل، المنتج، المرحلة).
    ///
    /// ليه مش فلتر عام: الكيانات دي مربوطة بسجلات تاريخية بعلاقات
    /// إجبارية، والفلتر العام كان هيخفي السجلات دي كمان (شوف التعليق في
    /// AppDbContext.OnModelCreating).
    ///
    /// القاعدة للي بيكتب ريبو جديد: أي دالة بترجّع قايمة للعرض أو
    /// الاختيار بتنادي الامتداد ده. أي دالة تاريخية (تقارير، أجور) **لأ**،
    /// عشان السجل القديم يفضل يعرض اسم صاحبه.
    /// </summary>
    public static class SoftDeleteQueryExtensions
    {
        /// <summary>بيشيل الصفوف المحذوفة ناعمًا من الاستعلام</summary>
        public static IQueryable<T> ExcludeDeleted<T>(this IQueryable<T> source)
            where T : ISoftDeletable
            => source.Where(e => !e.IsDeleted);
    }
}
