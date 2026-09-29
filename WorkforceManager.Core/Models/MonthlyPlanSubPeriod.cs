using System.ComponentModel.DataAnnotations;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// تقسيم اختياري لشهر MonthlyPlan لفترات فرعية مؤرخة ("خطة 1"، "خطة
    /// 2"...) — إضافي فوق الشهر الكامل، مش استبدال له. MonthlyPlan.SubPeriodId
    /// بيشاور هنا (0 = الشهر كامل، من غير صف حقيقي، من غير FK — شوف تعليق
    /// SubPeriodId).
    ///
    /// **مفيش WorkdayCount مخزّن هنا** (بعكس PlanPeriod) — أيام الشغل
    /// بتتحسب حيّة في MonthlyPlanTrackingService.GetTrackingAsync من
    /// StartDate/EndDate + جدول العطلات الحالي وقت العرض، زي MonthlyPlan
    /// بالظبط، عشان نتجنب مشكلة الرقم القديم اللي PlanPeriod بيعاني منها.
    /// </summary>
    public class MonthlyPlanSubPeriod
    {
        [Key]
        public int Id { get; set; }

        public int Year { get; set; }

        /// <summary>1-12 — نفس شهر كل MonthlyPlan بتاعتها، الفترة لازم تقع جواه</summary>
        public int Month { get; set; }

        public required string Label { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        /// <summary>ترتيب العرض بين فترات نفس الشهر</summary>
        public int SortOrder { get; set; }
    }
}
