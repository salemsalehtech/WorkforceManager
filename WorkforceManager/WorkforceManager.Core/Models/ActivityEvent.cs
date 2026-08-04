using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// حدث واحد في سجل العمليات: مين عمل إيه، إمتى، وليه.
    ///
    /// السجل ده **مش نسخة تانية** من حقول الحذف اللي على الكيان نفسه
    /// (<see cref="Interfaces.ISoftDeletable"/>). الفرق:
    ///   • حقول الكيان بتجاوب على "الصف ده اتشال ليه؟" وانت واقف عليه.
    ///   • السجل ده بيجاوب على "إيه اللي حصل في المصنع الأسبوع ده؟"
    ///     مرتب بالوقت وعبر كل الأنواع مع بعض.
    /// عشان كده الاتنين موجودين، وكل واحد بيتكتب مرة واحدة في نفس المعاملة.
    ///
    /// التصميم عام عن قصد (نوع + كيان + معرّف + بيانات إضافية) عشان أي
    /// حدث جديد يتضاف من غير عمود جديد ولا ترحيل.
    /// </summary>
    [Index(nameof(OccurredAt))]                          // العرض دايمًا مرتب بالوقت
    [Index(nameof(EventType))]                           // الفلترة بالنوع
    [Index(nameof(EntityType), nameof(EntityId))]        // "إيه اللي حصل للعامل ده؟"
    public class ActivityEvent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public ActivityEventType EventType { get; set; }

        /// <summary>
        /// نوع الكيان كنص ("Worker" / "Product" / "DailyProduction").
        /// نص مش enum عشان نوع جديد ميحتاجش ترحيل قاعدة بيانات.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>معرّف الكيان اللي الحدث حصل له (0 لو مالوش معرّف)</summary>
        public int EntityId { get; set; }

        /// <summary>
        /// اسم الكيان وقت الحدث — لقطة مش مرجع.
        /// السجل لازم يفضل مقروء حتى لو الكيان اتشال بعد كده.
        /// </summary>
        [MaxLength(200)]
        public string? EntityName { get; set; }

        /// <summary>اسم المستخدم اللي عمل العملية</summary>
        [Required]
        [MaxLength(100)]
        public string Actor { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; } = DateTime.Now;

        /// <summary>السبب اللي كتبه المستخدم (مطلوب في الحذف)</summary>
        [MaxLength(500)]
        public string? Reason { get; set; }

        /// <summary>
        /// تفاصيل إضافية خاصة بنوع الحدث — نص حر مقروء للبني آدم
        /// (مثال: "الأجر اتغيّر من 200 لـ 250 جنيه").
        ///
        /// نص مش JSON عن قصد: ده سجل بيتقرا بالعين في شاشة، مش بيتفكّ
        /// برمجيًا. JSON هنا كان هيضيف تعقيد من غير أي مستهلك.
        /// </summary>
        [MaxLength(1000)]
        public string? Details { get; set; }
    }
}
