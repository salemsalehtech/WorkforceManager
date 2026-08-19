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
    public class ProductionStage : SoftDeletableEntity
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

        /// <summary>
        /// مرحلة رص إدارية — موجودة كصف حقيقي (ليها ترتيب، تظهر في شاشة
        /// المنتج وفي شاشة الإنتاج اليومي زي أي مرحلة، آخر الترتيب) بس
        /// **برّه خط الإنتاج المحسوب تمامًا** (<c>ProductionLine.Active</c>):
        /// مستبعدة من نطاقات القطع (مفيش "من مرحلة X لمرحلة Y" يشملها)
        /// ومن حساب "أول/آخر مرحلة" في كل التقارير والرسوم البيانية.
        /// بطاقتها في شاشة الإنتاج اليومي بتاخد عامل (تاج، من غير قطع)
        /// بدل نطاق — شوف <see cref="WorkforceManager.Business.Services.ProductionFlowService"/>.
        ///
        /// PiecesPerWorkday لسه لازم يكون رقم موجب (قيد قاعدة البيانات)
        /// بس قيمته مالها معنى هنا — مش بتتقرا في أي حساب لأن المرحلة
        /// برّه الخط.
        /// </summary>
        public bool IsRackingStage { get; set; }

        /// <summary>
        /// معامل صعوبة يدوي (1.0 = عادي) — يُستخدم **فقط** في حساب ترتيب
        /// "أحسن عامل" الأسبوعي/الشهري (<see cref="WorkforceManager.Business.Services.WorkerRecognitionService"/>)،
        /// عشان مرحلة يومياتها منخفضة بطبيعتها (دقيقة/صعبة) ما تظلمش
        /// العامل عليها في الترتيب. مايتلمسش خالص في أي حساب أجر أو تقرير
        /// إنتاج — القيمة الحقيقية والأجر بيفضلوا كما هما تمامًا.
        /// </summary>
        public decimal DifficultyMultiplier { get; set; } = 1.0m;

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
