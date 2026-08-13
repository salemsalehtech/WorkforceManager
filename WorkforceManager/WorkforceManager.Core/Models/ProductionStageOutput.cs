using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// الإنتاج الفعلي الحقيقي لمرحلة في يوم — منفصل عن قطع العمال.
    ///
    /// قطعة العامل (<see cref="DailyProduction.PieceCount"/>) هي عدد
    /// ضرباته على المكنة، وأساس يوميته وأجره بس. الرقم هنا هو "من مرحلة
    /// X لـY أنتج N قطعة" اللي بيتكتب في نطاق شاشة الإنتاج اليومي —
    /// **الإنتاج الفعلي للمنتج**. الاتنين مايلزمش يتطابقوا: جزء من ضربات
    /// العامل بيتحول هالك أو مايكملش، وده طبيعي مش عطل.
    ///
    /// **صف واحد لكل مرحلة/يوم، وقيمته تراكمية** — زي
    /// <see cref="ProductionScrap"/> بالظبط: تسجيل تاني لنفس المرحلة/اليوم
    /// (رحلة تانية بعد إعادة فتح يوم مقفول مثلًا) بيجمع على الرقم الموجود
    /// مش يستبدله، فالفهرس على (ProductionStageId, Date) يونيك لضمان صف
    /// واحد تتجمّع فيه، لا صفوف متعددة.
    /// </summary>
    [Index(nameof(Date))]
    [Index(nameof(ProductionStageId), nameof(Date), IsUnique = true)]

    // فهرس مغطّي بنفس منطق DailyProduction/ProductionScrap: "الشغل
    // الواقف" بيجمع الإنتاج الفعلي من أول يوم مجمّعًا بالمرحلة. PieceCount
    // جوه الفهرس عشان المجموع يتحسب من غير رجوع للجدول.
    [Index(nameof(ProductionStageId), nameof(Date), nameof(PieceCount))]
    public class ProductionStageOutput
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(ProductionStage))]
        public int ProductionStageId { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "عدد القطع لازم يكون أكبر من صفر")]
        public int PieceCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>مين سجّله — للسجل والمراجعة</summary>
        [MaxLength(100)]
        public string? RecordedBy { get; set; }

        // ------- العلاقات -------

        public virtual ProductionStage ProductionStage { get; set; } = null!;
    }
}
