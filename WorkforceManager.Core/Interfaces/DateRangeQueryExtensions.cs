using System;
using System.Linq;

namespace WorkforceManager.Core.Interfaces
{
    /// <summary>
    /// أي كيان له تاريخ يومي واحد (حضور، إنتاج، جزاء، تعديل أجر، ساعات
    /// عمل) — عشان <see cref="DateRangeQueryExtensions.InDateRange{T}"/>
    /// تقدر تفلتر عليه من غير ما تعرف نوعه بالظبط.
    /// </summary>
    public interface IHasDate
    {
        DateTime Date { get; }
    }

    /// <summary>
    /// نطاق التاريخ (شامل الطرفين، على مستوى اليوم) — المكان الوحيد اللي
    /// بيكتب الشرط ده، نفس فكرة <see cref="SoftDeleteQueryExtensions.ExcludeDeleted{T}"/>.
    /// كان مكرر حرفيًا في 5 ريبو/9 دوال قبل كده.
    /// </summary>
    public static class DateRangeQueryExtensions
    {
        public static IQueryable<T> InDateRange<T>(this IQueryable<T> source, DateTime from, DateTime to)
            where T : IHasDate
        {
            var fromDate = from.Date;
            var toDate = to.Date;
            return source.Where(e => e.Date >= fromDate && e.Date <= toDate);
        }
    }
}
