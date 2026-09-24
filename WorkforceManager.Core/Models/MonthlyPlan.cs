using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// الكمية المخططة لمنتج واحد، لشهر واحد — القاعدة "مرة واحدة لكل
    /// منتج لكل شهر" (شوف الفهرس الفريد في AppDbContext على
    /// Product+Year+Month).
    ///
    /// **مفيش رقم "خطة عيلة" مخزّن في أي مكان** — خطة العيلة دايمًا مجموع
    /// خطط منتجاتها، محسوبة وقت العرض (MonthlyPlanService)، عشان تحل
    /// التضارب اللي كان في الشيت اليدوي القديم (مجموع الأجزاء مايطابقش
    /// رقم العيلة أحيانًا). لا تضيف عمود خطة على ProductFamily.
    /// </summary>
    public class MonthlyPlan
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public int Year { get; set; }

        /// <summary>1-12 — شوف HasCheckConstraint في AppDbContext</summary>
        public int Month { get; set; }

        public int PlannedQuantity { get; set; }

        public virtual Product Product { get; set; } = null!;
    }
}
