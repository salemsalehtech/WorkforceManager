using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// "لقطة نهاية اليوم" — حفظ وقتي لحالة المحقق، للرجوع له بعدين. حفظ +
    /// عرض بس (مفيش مقارنة تلقائية دلوقتي، شوف CLAUDE.md) — صف واحد لكل
    /// منتج ظاهر وقت الحفظ، كلهم بنفس TakenAt (دفعة واحدة).
    /// </summary>
    public class MonthlyPlanSnapshot
    {
        [Key]
        public int Id { get; set; }

        /// <summary>وقت اللقطة الحقيقي (ساعة النظام) — نفس القيمة على كل صفوف نفس الدفعة، بيتحدد "اللقطة" بيه</summary>
        public DateTime TakenAt { get; set; }

        /// <summary>
        /// الفترة (شهر تقويمي) اللي اللقطة دي بتاعتها — مختلفة عن TakenAt
        /// عمدًا: ممكن تاخد لقطة النهارده وانت واقف على شهر فات في تبويب
        /// "التخطيط" (لقطة الشهر ده)، فمينفعش نفلتر بمدى تاريخ TakenAt.
        /// </summary>
        public int Year { get; set; }
        public int Month { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        public int PlannedQuantity { get; set; }
        public int EffectiveAchieved { get; set; }

        public virtual Product Product { get; set; } = null!;
    }
}
