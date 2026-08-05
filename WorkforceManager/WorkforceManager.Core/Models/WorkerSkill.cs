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
        /// مستوى الإتقان القديم (مبتدئ / متمكن / خبير).
        ///
        /// النجوم (<see cref="Stars"/>) بقت هي التقييم المعتمد. العمود ده
        /// باقي عشان البيانات القديمة والـ seed اللي بيستخدمه، وبيتحدّث
        /// مع النجوم عشان الاتنين ميتناقضوش.
        /// </summary>
        public SkillLevel Level { get; set; } = SkillLevel.Proficient;

        // ------- التقييم: رأي المدير (نجوم) -------

        /// <summary>
        /// تقييم المدير من 1 لـ 5 نجوم — **رأي بني آدم، النظام مبيغيّروش أبدًا**.
        ///
        /// ده الرقم اللي بيتعرض وبيترتب بيه العمال في قوايم الاختيار.
        /// النظام بيقيس الأداء الفعلي في <see cref="MeasuredRatio"/> جنبه
        /// وبيقترح تعديل وقت المراجعة الشهرية، لكن القرار بيفضل للمدير:
        /// هو اللي شايف العامل وعارف الظروف اللي الأرقام مبتقولهاش
        /// (ماكينة بايظة، شغل صعب، عامل جديد بيتعلّم).
        ///
        /// 3 = بيعمل الكوتة المعيارية — نقطة البداية المحايدة.
        /// </summary>
        [Range(1, 5)]
        public int Stars { get; set; } = 3;

        /// <summary>آخر مرة المدير عدّل النجوم (لمعرفة التقييمات القديمة اللي محتاجة مراجعة)</summary>
        public DateTime? StarsUpdatedAt { get; set; }

        /// <summary>مين آخر واحد عدّل النجوم</summary>
        [MaxLength(100)]
        public string? StarsUpdatedBy { get; set; }

        // ------- التقييم: الأداء المقاس (النظام) -------

        /// <summary>
        /// نسبة أداء العامل الفعلية على المرحلة دي: إنتاجه ÷ اليومية
        /// المعيارية. 1.0 = بيعمل الكوتة بالظبط، 1.3 = بيعملها وزيادة 30%.
        ///
        /// النسبة مش عدد قطع عن قصد: المراحل كوتاتها مختلفة تمامًا
        /// (5000 قطعة في مرحلة و80 في مرحلة تانية)، فمقارنة الأعداد
        /// الخام بين مرحلتين مالهاش أي معنى.
        ///
        /// الرقم ده **دليل مش حكم**: النظام بيحسبه ويعرضه جنب النجوم،
        /// والمدير هو اللي يقرر يغيّر النجوم ولا لأ.
        /// </summary>
        public decimal MeasuredRatio { get; set; } = 1.0m;

        /// <summary>آخر مرة النظام قاس الأداء (null = لسه مافيش إنتاج كفاية)</summary>
        public DateTime? MeasuredAt { get; set; }

        /// <summary>
        /// عدد أيام الشغل اللي القياس اتبنى عليها.
        ///
        /// بيتعرض جنب الرقم عشان المدير يعرف يثق فيه قد إيه: قياس مبني
        /// على يومين مش زي قياس مبني على عشرين.
        /// </summary>
        public int MeasuredDays { get; set; }

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
        public virtual ProductionStage ProductionStage { get; set; } = null!;
    }
}
