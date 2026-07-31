using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// يمثل مرحلة تصنيع تابعة لمنتج معين، مع "اليومية" الخاصة بها.
    /// هام جدًا: نفس اسم المرحلة (مثلاً "دبله") ممكن يتكرر في أكتر
    /// من منتج، لكن كل صف هنا مستقل بيوميته الخاصة داخل منتجه فقط.
    /// اليومية = عدد القطع التي تساوي "يومية عمل كاملة واحدة"
    /// في هذه المرحلة تحديدًا لهذا المنتج. مثال: لو اليومية = 5000،
    /// فعامل أنتج 5000 قطعة يبقى عمل يومية واحدة، ولو أنتج 10000
    /// يبقى عمل يوميتين، وهكذا (قسمة، مش سعر بالجنيه).
    /// </summary>
    public class ProductionStage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "اسم المرحلة مطلوب")]
        [MaxLength(100)]
        public string StageName { get; set; } = string.Empty;

        /// <summary>ترتيب المرحلة داخل خط الإنتاج (اختياري، يفيد في عرض المراحل بترتيبها المنطقي)</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// عدد القطع التي تساوي يومية عمل كاملة واحدة في هذه المرحلة،
        /// خاص بهذا المنتج فقط (نفس اسم المرحلة في منتج آخر له يومية مختلفة تمامًا).
        /// </summary>
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "اليومية يجب أن تكون رقمًا موجبًا أكبر من صفر")]
        public int PiecesPerWorkday { get; set; }

        public bool IsActive { get; set; } = true;

        // ------- العلاقات -------

        public virtual Product Product { get; set; } = null!;

        /// <summary>كل العمال الذين يجيدون تنفيذ هذه المرحلة تحديدًا</summary>
        public virtual ICollection<WorkerSkill> QualifiedWorkers { get; set; } = new List<WorkerSkill>();

        /// <summary>كل سجلات الإنتاج اليومي المسجّلة على هذه المرحلة</summary>
        public virtual ICollection<DailyProduction> ProductionRecords { get; set; } = new List<DailyProduction>();

        [NotMapped]
        public string DisplayName => $"{StageName} — {Product?.Name}";
    }
}
