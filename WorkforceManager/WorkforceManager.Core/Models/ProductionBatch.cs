using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WorkforceManager.Core.Enums;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// دفعة إنتاج: كمية قطع من منتج واحد بتمشي في خط الإنتاج مرحلة ورا
    /// مرحلة، وممكن ما تخلصش في يوم واحد.
    ///
    /// ليه الكيان ده موجود: قبل كده النظام كان بيسجل "المرحلة دي اتعمل
    /// عليها 300 قطعة النهارده" وخلاص. مكانش فيه أي رابط بين الـ300
    /// الواقفة عند مرحلة 5 النهارده والـ300 اللي هتكمّل بكرة، فمكانش
    /// ينفع تقول "المنتج ده لسه مخلصش" ولا تعرف واقف عند فين بالظبط.
    /// الدفعة هي الرابط ده.
    ///
    /// القطع دايمًا في دفعة: دفعة بتتفتح لما نطاق يبدأ من أول مرحلة في
    /// الخط، وبتتقفل لما توصل لآخر مرحلة نشطة. أي نطاق بيبدأ من نص الخط
    /// لازم يكمّل دفعة مفتوحة موجودة — عشان مستحيل قطع تتعد مرتين أو
    /// تظهر من العدم.
    /// </summary>
    [Index(nameof(ProductId), nameof(Status))] // "الواقف من المنتج ده" — أكتر استعلام
    [Index(nameof(StartedDate))]
    [Index(nameof(CompletedDate))]
    public class ProductionBatch
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }

        /// <summary>اليوم اللي الدفعة دخلت فيه الخط (مش يوم ما خلصت)</summary>
        public DateTime StartedDate { get; set; }

        /// <summary>
        /// عدد القطع في الدفعة دلوقتي. بيقل لو الدفعة اتقسمت — يعني لو 300
        /// واقفين وكمّلنا 200 بس، الـ200 بتفضل في الدفعة دي والـ100 بتخرج
        /// في دفعة جديدة (<see cref="SplitFromBatchId"/>).
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "عدد قطع الدفعة يجب أن يكون أكبر من صفر")]
        public int Quantity { get; set; }

        /// <summary>
        /// آخر مرحلة الدفعة عدّتها. null = لسه ما دخلتش أي مرحلة.
        ///
        /// بنخزّن الـ Id مش رقم الترتيب عن قصد: ترتيب المراحل بيتغيّر
        /// (MoveStageAsync بيعيد ترقيم الخط كله)، فرقم مخزّن كان هيبوظ
        /// موقع كل الدفعات المفتوحة. الـ Id ثابت، والترتيب بيتقرا من
        /// المرحلة نفسها وقت الحاجة.
        /// </summary>
        [ForeignKey(nameof(LastCompletedStage))]
        public int? LastCompletedStageId { get; set; }

        public BatchStatus Status { get; set; } = BatchStatus.Open;

        /// <summary>يوم ما عدّت آخر مرحلة (null طول ما هي مفتوحة)</summary>
        public DateTime? CompletedDate { get; set; }

        /// <summary>
        /// الدفعة دي اتقسمت من دفعة تانية (تكميل جزئي). بتفضل للتتبع بس —
        /// الدفعتين مستقلتين تمامًا بعد القسمة.
        /// </summary>
        [ForeignKey(nameof(SplitFromBatch))]
        public int? SplitFromBatchId { get; set; }

        /// <summary>
        /// رصيد افتتاحي: القطع دي عدّت المراحل اللي قبل نقطة دخولها **برّه
        /// النظام** — يا إما اتسجلت قبل ما نظام الدفعات يشتغل، يا إما شغل
        /// جه من برّه.
        ///
        /// ليه لازم يتعلّم: مراحلها الأولى مالهاش سجلات إنتاج، فلو حد راجع
        /// "قطع مرحلة 7 = مجموع إنتاج مراحل 1..6" الحسبة مش هتقفل. العلامة
        /// دي بتفسّر الفرق بدل ما يبان كخطأ في البيانات.
        /// </summary>
        public bool IsOpeningBalance { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }

        // ------- العلاقات -------

        public virtual Product Product { get; set; } = null!;
        public virtual ProductionStage? LastCompletedStage { get; set; }
        public virtual ProductionBatch? SplitFromBatch { get; set; }

        /// <summary>سجلات الإنتاج اللي حرّكت الدفعة دي (لكل عامل/مرحلة/يوم)</summary>
        public virtual ICollection<DailyProduction> Productions { get; set; } = new List<DailyProduction>();

        // ------- خصائص محسوبة -------

        /// <summary>لسه في الخط وبتترحّل لبكرة</summary>
        [NotMapped]
        public bool IsOpen => Status == BatchStatus.Open;

        /// <summary>
        /// خلصت في يوم غير اليوم اللي بدأت فيه — يعني اتّرحّلت على الأقل
        /// مرة. التقرير اليومي بيستخدمها عشان يقول "منها كذا مرحّلة".
        /// </summary>
        [NotMapped]
        public bool WasCarriedOver =>
            Status == BatchStatus.Completed && CompletedDate is { } done && done.Date != StartedDate.Date;
    }
}
