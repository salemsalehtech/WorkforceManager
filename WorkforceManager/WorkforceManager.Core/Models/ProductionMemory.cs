using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// خطة إنتاج متأجّلة — "المنتج ده ناوي أعمله يوم كذا، بالترتيب ده،
    /// والملاحظات دي".
    ///
    /// **الحاجة الوحيدة في البرنامج كله اللي بترتيبها يغيّر معنى نطاق
    /// الإنتاج.** لما المستخدم يدوس "ابدأ الآن" على تذكير، شاشة الإنتاج
    /// اليومي بتفتح والتحقق من النطاقات ("من مرحلة X لمرحلة Y") بيمشي
    /// على <see cref="Stages"/> بدل ترتيب المنتج الحقيقي — **للجلسة دي
    /// بس**. أي حتة تانية في البرنامج (التقارير، شاشة المنتجات،
    /// PendingWorkService، وحتى جلسة إنتاج عادية لنفس المنتج) بتفضل
    /// على ترتيب المنتج الدائم زي ما هي. شوف ProductionLine.CustomOrder
    /// وتعليق CLAUDE.md على الاستثناء ده.
    ///
    /// **مش كيان بحذف ناعم عن قصد**: الحذف هنا معناه "غيّرت رأيي، شيل
    /// الخطة دي" — مفيش سجل تاريخي بيشاور عليها ولا تقرير بيقراها.
    /// اللي اتنفّذ فعلاً بيتحفظ بـ<see cref="CompletedAt"/> في قايمة
    /// المنجزة بدل ما يتمسح.
    /// </summary>
    // الاستعلام الوحيد المتكرر: "المتأخرة والنهارده" عند كل بدء تشغيل.
    // فهرس مطابق له بالظبط — الجدول صغير بطبيعته (خطط المستخدم)، بس
    // ده استعلام بدء تشغيل ومكلّفش حاجة
    [Index(nameof(CompletedAt), nameof(RemindOn))]
    public class ProductionMemory
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;

        /// <summary>ملاحظات المستخدم الحرة عن الخطة دي</summary>
        [MaxLength(2000)]
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// يوم التذكير (بدون وقت). التذكير بيضرب أول ما البرنامج يتفتح
        /// **في اليوم ده أو بعده** — مش في اليوم بالظبط: لو المصنع قفل
        /// أسبوع، الخطة المتأخرة لازم تفضل تفكّر مش تعدّي في صمت.
        /// </summary>
        public DateTime RemindOn { get; set; }

        /// <summary>
        /// لحظة الضغط على "ابدأ الآن" — null معناها الخطة لسه نشطة.
        /// بيتحط **بمجرد فتح الشاشة**، بغض النظر عن إذا كان المستخدم
        /// حفظ إنتاج فعلاً ولا لأ (قرار مؤكد): التذكير شغله يفكّر، وهو
        /// عمل شغله خلاص لما وصّل المستخدم للشاشة.
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>ترتيب المراحل المخطط له — مرتّب بـ Position</summary>
        public ICollection<ProductionMemoryStage> Stages { get; set; } = new List<ProductionMemoryStage>();
    }
}
