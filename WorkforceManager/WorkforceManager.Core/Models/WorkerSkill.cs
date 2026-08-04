using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// جدول ربط (Many-to-Many) بين العامل والمرحلة: يوضح "هذا العامل
    /// يجيد تنفيذ هذه المرحلة تحديدًا في هذا المنتج". هذا هو الأساس
    /// الذي تُبنى عليه ميزة "ابحث عن اسم عامل واعرف بيعرف يعمل إيه".
    /// </summary>
    public class WorkerSkill
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        [Required]
        [ForeignKey(nameof(ProductionStage))]
        public int ProductionStageId { get; set; }

        /// <summary>
        /// مستوى الإتقان (مبتدئ / متمكن / خبير).
        ///
        /// ده المستوى **المعروض** — بيتشتق من <see cref="RatingValue"/>
        /// عن طريق SkillRatingService، مش بيتكتب بالإيد لوحده. موجود
        /// كعمود مخزّن عشان الفرز والفلترة يتمّوا في الداتابيز.
        /// </summary>
        public SkillLevel Level { get; set; } = SkillLevel.Proficient;

        // ------- التقييم -------

        /// <summary>
        /// نسبة أداء العامل على المرحلة دي: إنتاجه الفعلي ÷ اليومية
        /// المعيارية للمرحلة. 1.0 = بيعمل الكوتة بالظبط، 1.3 = بيعملها
        /// وزيادة 30%، 0.7 = بيعمل 70% منها.
        ///
        /// النسبة مش رقم مطلق عن قصد: المراحل كوتاتها مختلفة تمامًا
        /// (5000 قطعة في مرحلة و80 في مرحلة تانية)، فمقارنة الأعداد
        /// الخام بين مرحلتين مالهاش أي معنى.
        /// </summary>
        public decimal RatingValue { get; set; } = 1.0m;

        /// <summary>الرقم الحالي جه بالإيد ولا النظام حسبه</summary>
        public SkillRatingSource RatingSource { get; set; } = SkillRatingSource.Manual;

        /// <summary>
        /// آخر قيمة حطّها المستخدم بإيده.
        ///
        /// بتتحفظ حتى بعد ما النظام يحسب قيمة تلقائية فوقها: ده اللي
        /// بيخلي الواجهة تقدر تقول "انت قلت 1.2 والنظام لقى 0.9" بدل
        /// ما التقدير البشري يضيع من غير أثر.
        /// </summary>
        public decimal? LastManualValue { get; set; }

        /// <summary>آخر مرة النظام أعاد الحساب (null = لسه ما اتحسبش تلقائيًا)</summary>
        public DateTime? LastAutoCalculatedAt { get; set; }

        /// <summary>
        /// عدد أيام الإنتاج اللي الحساب التلقائي اتبنى عليها.
        ///
        /// بيتعرض جنب الرقم عشان المستخدم يعرف يثق فيه قد إيه: تقييم
        /// مبني على يومين مش زي تقييم مبني على عشرين.
        /// </summary>
        public int AutoSampleDays { get; set; }

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
        public virtual ProductionStage ProductionStage { get; set; } = null!;
    }
}
