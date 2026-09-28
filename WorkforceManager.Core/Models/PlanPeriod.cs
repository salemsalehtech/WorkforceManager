using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// فترة خطة إنتاج بتاريخ بداية/نهاية مخصص (مش شهر تقويمي) — بديل موازي
    /// لـ MonthlyPlan (الخطة الشهرية)، مش استبدال ليه. الاتنين بيتعايشوا
    /// جنب بعض عن قصد: MonthlyPlan وكل اللي مبني عليه (المحقق/التتبع/تصدير
    /// Excel/كارت الهوم) شغالين على Year+Month، وإعادة بنائهم على فترة
    /// مخصصة كان هيكسرهم من غير داعي — شوف CLAUDE.md.
    ///
    /// WorkdayCount بيتحسب مرة واحدة وقت الإنشاء (WorkCalendarRules + جدول
    /// MonthlyWorkCalendarHoliday الموجود، بيتشارك بين الفيتشرين لأنه أصلاً
    /// تاريخ عام مش مربوط بشهر) ويتخزن هنا — عطلة تتضاف بعد إنشاء الفترة
    /// مابتحدّثش الرقم المخزّن تلقائيًا.
    /// </summary>
    public class PlanPeriod
    {
        [Key]
        public int Id { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public int WorkdayCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<PlanPeriodTarget> Targets { get; set; } = new List<PlanPeriodTarget>();
    }
}
