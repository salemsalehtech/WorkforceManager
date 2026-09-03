using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// عملية استخدام/إكمال فعلية من رصيد أولي — سجل واحد لكل مرة العامل
    /// كمّل جزء من الرصيد في يوم معين.
    ///
    /// **قاعدة "مايتحسبش مرتين" (القاعدة الأهم في الفيتشر كله)**:
    /// الاستخدام بيتحسب في يومية وأجر العامل بتاريخ الإكمال الفعلي
    /// (<see cref="UsedDate"/>) عن طريق سجل <see cref="DailyProduction"/>
    /// مرتبط (<see cref="DailyProductionId"/>)، لكن الإنتاج الفعلي الحقيقي
    /// للمرحلة بيتسجّل بتاريخ الإنتاج **الأصلي** لصاحب الرصيد
    /// (<see cref="InitialBalance.OriginalDate"/>) على
    /// <see cref="ProductionStageOutput"/> — تراكميًا زي أي رحلة تانية
    /// لنفس اليوم. عشان كده سجل DailyProduction المرتبط هنا لازم يتحطله
    /// <see cref="DailyProduction.IsBalanceCompletion"/> = true، بالظبط
    /// زي <see cref="DailyProduction.IsRework"/>: يتحسب في اليومية
    /// والأجر عادي، بس يتستبعد من رجوع
    /// <see cref="WorkforceManager.Business.Services.ProductionStageOutputService"/>
    /// للحساب القديم — وإلا كان هيتحسب إنتاج فعلي جديد يوم الإكمال
    /// (تكرار عد بالظبط زي مشكلة الـ10,000 اللي اتحلت قبل الفيتشر ده).
    ///
    /// **بالظبط واحد من <see cref="DailyProductionId"/> و
    /// <see cref="ProductionScrapId"/> لازم يكون موجود، مش الاتنين
    /// ومش ولا واحد فيهم.** سحب عادي (عامل بيكمّل شغل) بيسجّل
    /// DailyProduction ومالوش ProductionScrap؛ سحب لهالك (تحويل جزء
    /// من الرصيد لهالك بدل إكمال) بيسجّل ProductionScrap ومالوش
    /// DailyProduction ولا عامل (<see cref="WorkerId"/> بيبقى null).
    /// القاعدة دي متفروضة في طبقة الـ Service مش في قاعدة البيانات.
    /// </summary>
    [Index(nameof(InitialBalanceId))]
    public class InitialBalanceUsage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(InitialBalance))]
        public int InitialBalanceId { get; set; }

        /// <summary>النطاق اللي اتاخدت منه الكمية (اختياري — ممكن الرصيد ما كانش مقسّم لنطاقات)</summary>
        [ForeignKey(nameof(Range))]
        public int? InitialBalanceRangeId { get; set; }

        /// <summary>تاريخ الإكمال الفعلي (يوم العامل اشتغل فيه فعلًا، مش تاريخ الإنتاج الأصلي)</summary>
        [Required]
        public DateTime UsedDate { get; set; } = DateTime.Today;

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "عدد القطع المستخدمة يجب أن يكون رقمًا موجبًا")]
        public int Quantity { get; set; }

        /// <summary>العامل اللي كمّل — null لو الاستخدام سحب لهالك (مفيش عامل وقتها)</summary>
        [ForeignKey(nameof(Worker))]
        public int? WorkerId { get; set; }

        /// <summary>آخر مرحلة اتكملت في عملية الاستخدام دي</summary>
        [Required]
        [ForeignKey(nameof(ProductionStage))]
        public int ProductionStageId { get; set; }

        /// <summary>سجل الإنتاج (اليومية/الأجر) المرتبط بعملية الإكمال دي — واحد لكل استخدام، null لو سحب لهالك</summary>
        [ForeignKey(nameof(DailyProduction))]
        public int? DailyProductionId { get; set; }

        /// <summary>سجل الهالك المرتبط بعملية السحب دي — null لو الاستخدام إكمال إنتاج عادي</summary>
        [ForeignKey(nameof(ProductionScrap))]
        public int? ProductionScrapId { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>مين سجّل الاستخدام — للسجل والمراجعة</summary>
        [MaxLength(100)]
        public string? RecordedBy { get; set; }

        // ------- العلاقات -------

        public virtual InitialBalance InitialBalance { get; set; } = null!;

        public virtual InitialBalanceRange? Range { get; set; }

        public virtual Worker? Worker { get; set; }

        public virtual ProductionStage ProductionStage { get; set; } = null!;

        public virtual DailyProduction? DailyProduction { get; set; }

        public virtual ProductionScrap? ProductionScrap { get; set; }
    }
}
