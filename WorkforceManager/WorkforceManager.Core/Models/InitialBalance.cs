using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// "رصيد أولي" لمنتج: كمية قطع تخص تاريخ إنتاج أصلي معيّن ولسه ماكملتش،
    /// اتحطت جنب عشان تتكمّل بعدين (يوم أو أيام تانية) من غير ما تتحسب
    /// إنتاج جديد يوم ما تتكمّل — شوف <see cref="InitialBalanceUsage"/>
    /// لتفاصيل قاعدة "مايتحسبش مرتين".
    ///
    /// **مش نسخة من ProductionBatches القديم** (اتشال بالكامل في
    /// 2026-08-02 لصالح نظام النطاقات + الإنتاج الفعلي التراكمي الحالي).
    /// الرصيد هنا مبني فوق نفس النظام ده: بيستخدم <see cref="ProductionStage"/>
    /// ونطاقاته زي أي رحلة إنتاج عادية، وبيسجّل الإنتاج الفعلي على
    /// <see cref="ProductionStageOutput"/> بتاريخ الإنتاج **الأصلي** مش
    /// تاريخ الإكمال — مفيش جدول حالة/دفعة مستقل زي القديم.
    ///
    /// **حذف ناعم** عن قصد: الرصيد سجل تاريخي (شوف قاعدة رقم 8 في
    /// الفيتشر: "Created → Source → Original date → Ranges → Used →
    /// Completed → Edited/Deleted") لازم يفضل ممكن تتبعه حتى بعد حذفه.
    /// </summary>
    public class InitialBalance : SoftDeletableEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "اسم الرصيد مطلوب")]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Notes { get; set; }

        /// <summary>إجمالي كمية الرصيد وقت إنشائه (ثابتة — المتاح بيتحسب بطرح الاستخدامات منها)</summary>
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "كمية الرصيد يجب أن تكون رقمًا موجبًا")]
        public int Quantity { get; set; }

        /// <summary>تاريخ الإنتاج الأصلي اللي القطع دي ملكه فعلًا (مش تاريخ إنشاء الرصيد بالضرورة لو اتسجل بعدين)</summary>
        [Required]
        public DateTime OriginalDate { get; set; } = DateTime.Today;

        [Required]
        public InitialBalanceSource Source { get; set; } = InitialBalanceSource.Manual;

        /// <summary>
        /// سجل الإنتاج اليومي الأصلي اللي الرصيد ده اتقطع منه (لو المصدر
        /// DailyProduction). SetNull: لو السجل الأصلي اتصحّح أو اتحذف
        /// بعدين، الرصيد نفسه يفضل قائم بكل بياناته — الرابط بس بيروح.
        /// </summary>
        [ForeignKey(nameof(OriginalDailyProduction))]
        public int? OriginalDailyProductionId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>مين أنشأ الرصيد — للسجل والمراجعة</summary>
        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        // ------- العلاقات -------

        public virtual Product Product { get; set; } = null!;

        public virtual DailyProduction? OriginalDailyProduction { get; set; }

        public virtual ICollection<InitialBalanceRange> Ranges { get; set; } = new List<InitialBalanceRange>();

        public virtual ICollection<InitialBalanceUsage> Usages { get; set; } = new List<InitialBalanceUsage>();

        /// <summary>
        /// مجموع القطع اللي اتاخدت من الرصيد لحد دلوقتي. **مش كل صف
        /// Usages بيتحسب هنا** — نفس قاعدة
        /// <see cref="InitialBalanceRangeMath.UsedQuantity"/> بالظبط (سحب
        /// هالك بيتحسب دايمًا، سحب إكمال إنتاج بس لو وصل مرحلة خروج
        /// نطاقه)، مجمّعة على كل نطاقات الرصيد. عشان كده يفضل صحيح حتى لو
        /// <see cref="Usages"/> فيها صفوف لمراحل وسيطة (اتضافت لغرض العرض/
        /// التتبع بس، شوف InitialBalanceService.WriteUsageRowsAsync) —
        /// من غيرها القطعة كانت هتتحسب مرتين وهي عدّية على أكتر من مرحلة.
        /// </summary>
        [NotMapped]
        public int UsedQuantity =>
            Ranges.Sum(r => InitialBalanceRangeMath.UsedQuantity(r, Usages)) +
            Usages.Where(u => u.InitialBalanceRangeId == null).Sum(u => u.Quantity);

        /// <summary>الكمية اللي لسه متاحة = الإجمالي ناقص المستخدَم</summary>
        [NotMapped]
        public int RemainingQuantity => Quantity - UsedQuantity;

        /// <summary>
        /// الحالة محسوبة دايمًا من الاستخدامات، مش مخزّنة — نفس فلسفة
        /// DailyProduction.WorkdaysCompleted (تفادي عدم تطابق مع أي
        /// تعديل لاحق على الاستخدامات).
        /// </summary>
        [NotMapped]
        public InitialBalanceStatus Status =>
            UsedQuantity <= 0 ? InitialBalanceStatus.Available :
            UsedQuantity >= Quantity ? InitialBalanceStatus.Completed :
            InitialBalanceStatus.PartiallyUsed;
    }
}
