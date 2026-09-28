using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// الكمية المخططة لمنتج واحد، لفترة مخصصة واحدة (PlanPeriod) — القاعدة
    /// "مرة واحدة لكل منتج لكل فترة" (شوف الفهرس الفريد في AppDbContext على
    /// PlanPeriodId+ProductId). نفس شكل MonthlyPlan بالظبط، بدون Year/Month/
    /// DailyTargetQuantity — دول مش جزء من المرحلة دي.
    /// </summary>
    public class PlanPeriodTarget
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(PlanPeriod))]
        public int PlanPeriodId { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public int PlannedQuantity { get; set; }

        public virtual PlanPeriod PlanPeriod { get; set; } = null!;

        public virtual Product Product { get; set; } = null!;
    }
}
