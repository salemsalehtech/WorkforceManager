using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// قطع اتشالت من الخط ومش هتتكمّل — الهالك.
    ///
    /// **المرحلة هنا معناها: آخر مرحلة القطعة خلصتها قبل ما تتشال.**
    /// التعريف ده هو اللي بيخلي قاعدة واحدة تغطي الحالتين:
    ///
    ///   • هالك في نص الخط — 5000 خلصوا "تشطيب" و4500 بس دخلوا
    ///     "تغليف". الـ500 هالك على "تشطيب": بيتشالوا من الشغل الواقف
    ///     قدام "تغليف"، والإنتاج التام مايتأثرش لأنهم أصلاً ماوصلوش
    ///     آخر الخط.
    ///
    ///   • هالك بعد الخط — 4500 خلصوا آخر مرحلة والجودة رفضت 100.
    ///     الـ100 هالك على **آخر مرحلة**: بيتخصموا من الإنتاج التام
    ///     مباشرة، لأن التام = إنتاج آخر مرحلة ناقص هالكها.
    ///
    /// **مالوش أي علاقة بالأجور** — نفس قاعدة الشغل الواقف. عمال
    /// المراحل الأولى اشتغلوا على الـ5000 فعلًا وخدوا يوميتهم عليها،
    /// وعمال المراحل اللي بعدها سجّلوا 4500 لأن ده اللي وصلهم. مفيش
    /// حد بياخد زيادة ولا ناقص، والهالك رقم إداري مش خصم.
    ///
    /// **مفيش عامل على السجل** عن قصد: القطعة عدّت على مراحل كتير قبل
    /// ما تتشال، فنسبها لعامل واحد قرار إداري مش حقيقة في البيانات.
    /// </summary>
    [Index(nameof(Date))]
    [Index(nameof(ProductionStageId), nameof(Date))]
    public class ProductionScrap
    {
        [Key]
        public int Id { get; set; }

        /// <summary>آخر مرحلة القطعة خلصتها قبل ما تتشال</summary>
        [Required]
        [ForeignKey(nameof(ProductionStage))]
        public int ProductionStageId { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "عدد قطع الهالك يجب أن يكون رقمًا موجبًا")]
        public int PieceCount { get; set; }

        /// <summary>سبب من القايمة — بيفضل null لو السبب اتشال من الإعدادات</summary>
        [ForeignKey(nameof(Reason))]
        public int? ScrapReasonId { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>مين سجّله — للسجل والمراجعة</summary>
        [MaxLength(100)]
        public string? RecordedBy { get; set; }

        // ------- العلاقات -------

        public virtual ProductionStage ProductionStage { get; set; } = null!;

        public virtual ScrapReason? Reason { get; set; }
    }
}
