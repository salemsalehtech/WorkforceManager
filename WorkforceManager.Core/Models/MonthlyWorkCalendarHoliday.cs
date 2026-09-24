using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// عطلة يدوية إضافية (غير الجمعة، المستبعدة دايمًا زي باقي البرنامج) —
    /// يوم واحد بيضيفه المستخدم لشهر معيّن (عيد، صيانة المصنع...). بيُستخدم
    /// بس في حساب أيام الشغل الكلية/المنقضية/الباقية لشاشة الخطة الشهرية،
    /// شوف WorkCalendarRules.
    /// </summary>
    public class MonthlyWorkCalendarHoliday
    {
        [Key]
        public int Id { get; set; }

        /// <summary>التاريخ الكامل (مش بس يوم/شهر) — عطلة سنة بعينها، مش متكررة تلقائيًا كل سنة</summary>
        public DateTime Date { get; set; }

        [MaxLength(150)]
        public string? Reason { get; set; }
    }
}
