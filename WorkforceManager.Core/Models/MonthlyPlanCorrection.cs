using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// "تصليحات" — تعديل يدوي على المحقق ليوم واحد لمنتج واحد. **مش مشتقة
    /// من أي رحلة إنتاج ولا من DailyProduction.IsRework (عمال الإعادة) —
    /// دي عمليًا رقم بيكتبه المستخدم بإيده، منفصل تمامًا**. موجب (زيادة
    /// المحقق المعلن) أو سالب (نقصان).
    ///
    /// Unique على (ProductId, Date) — يوم واحد بيتسجل مرة واحدة بس؛ تعديله
    /// بيحدّث نفس الصف (Upsert)، مش صف جديد يتجمّع مع القديم.
    /// </summary>
    public class MonthlyPlanCorrection
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public DateTime Date { get; set; }

        public int Quantity { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }

        public virtual Product Product { get; set; } = null!;
    }
}
