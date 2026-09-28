using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// "الإنتاج اليومي" — رقم يدوي صرف بيكتبه المستخدم بنفسه لمنتج واحد في
    /// يوم واحد، **بديل** لحساب المحقق التلقائي القديم (كان بييجي من
    /// DailyProductionReportService)، مش تعديل عليه — شوف CLAUDE.md.
    ///
    /// **مختلفة تمامًا عن MonthlyPlanCorrection**: دي "الرقم نفسه" (الكمية
    /// المطلقة)، مش دلتا/تعديل فوق رقم تاني. الإنتاج الحقيقي (DailyProduction)
    /// بيُستخدم بس كمرجع اختياري (زرار "استخدم نفس الرقم الحقيقي" + تحذير
    /// الاختلاف) — نسخة مرة واحدة وقت الإدخال، مش ربط حي، ومفيش أي تأثير
    /// عكسي على الأجور أو رحلة الإنتاج.
    ///
    /// مرة واحدة بس لكل منتج لكل يوم — القاعدة زي MonthlyPlan/MonthlyPlanCorrection
    /// بالظبط (شوف الفهرس الفريد في AppDbContext).
    /// </summary>
    public class MonthlyPlanDailyEntry
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public DateTime Date { get; set; }

        public int Quantity { get; set; }

        /// <summary>آخر وقت اتكتب/اتعدّل فيه الرقم ده — أساس "آخر تحديث" في الشاشة، شوف MonthlyPlanTrackingService.GetLastUpdatedAsync</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public virtual Product Product { get; set; } = null!;
    }
}
