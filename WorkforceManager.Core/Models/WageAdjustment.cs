using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// تعديل أجر بالجنيه على عامل في تاريخ معين: سلفة (خصم) أو حافز (إضافة).
    /// مستقل عن الإنتاج والحضور والجزاءات — بيمثّل حركة فلوس مباشرة على
    /// أجر العامل. بيدخل في حساب صافي الأجر في كشف الفترة:
    /// الأجر = (صافي اليوميات × سعر اليومية) + الحوافز − السلف.
    /// حذفه hard delete للتصحيح (زي الجزاءات)، وبيتمسح مع العامل.
    /// </summary>
    // فهرس (WorkerId, Date) لتسريع تجميع تعديلات العامل خلال فترة معينة.
    // معرّف بـ fluent API في AppDbContext.OnModelCreating
    public class WageAdjustment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        /// <summary>تاريخ الحركة — بيحدد الفترة اللي هتتأثر في كشف الأجور</summary>
        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>نوع التعديل: سلفة (خصم) أو حافز (إضافة)</summary>
        [Required]
        public WageAdjustmentType Type { get; set; } = WageAdjustmentType.Advance;

        /// <summary>المبلغ بالجنيه (دائمًا موجب — الاتجاه بيحدده النوع)</summary>
        [Required]
        [Range(0.01, 9999999, ErrorMessage = "المبلغ لازم يكون أكبر من صفر")]
        public decimal AmountEgp { get; set; }

        /// <summary>سبب/بيان الحركة (مثال: سلفة نص الشهر، مكافأة تارجت)</summary>
        [MaxLength(300)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;

        /// <summary>الأثر الصافي بالجنيه على الأجر: الحافز موجب، السلفة سالبة</summary>
        [NotMapped]
        public decimal SignedAmountEgp => Type == WageAdjustmentType.Bonus ? AmountEgp : -AmountEgp;
    }
}
