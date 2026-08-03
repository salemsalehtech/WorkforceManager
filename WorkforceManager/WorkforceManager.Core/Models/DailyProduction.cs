using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// يمثل سجل إنتاج يومي واحد: عدد القطع التي أنجزها عامل معين
    /// في مرحلة معينة (تابعة لمنتج معين) في تاريخ معين.
    /// هذا هو النموذج الذي تُبنى عليه كل الحسابات (عدد اليوميات المنجزة)
    /// والتقييمات (مقارنة العامل بزملائه).
    /// </summary>
    // فهرس مركّب (WorkerId, ProductionStageId, Date) لتسريع استعلامات
    // "إنتاج عامل معين في يوم معين" التي ستُستخدم بكثرة في التقارير والتقييم
    [Index(nameof(WorkerId), nameof(ProductionStageId), nameof(Date))]
    public class DailyProduction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Worker))]
        public int WorkerId { get; set; }

        [Required]
        [ForeignKey(nameof(ProductionStage))]
        public int ProductionStageId { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "عدد القطع يجب أن يكون رقمًا موجبًا")]
        public int PieceCount { get; set; }

        /// <summary>
        /// اليومية وقت التسجيل (Snapshot). تُنسخ من
        /// ProductionStage.PiecesPerWorkday عند الإدخال، بدل الاعتماد
        /// المباشر على اليومية الحالية في جدول المراحل. السبب: لو غيّر
        /// مدير القسم اليومية بعدين، السجلات القديمة المحسوبة تفضل
        /// صحيحة ومحفوظة زي ما كانت وقت التنفيذ الفعلي.
        /// </summary>
        public int PiecesPerWorkdayAtEntry { get; set; }

        /// <summary>
        /// عدد "اليوميات" التي أنجزها العامل في هذا السجل = عدد القطع
        /// ÷ اليومية. رقم عشري لأنه ممكن يعمل يومية ونص مثلاً
        /// (Computed Property، غير مخزّن كعمود منفصل لتفادي عدم التطابق).
        /// </summary>
        [NotMapped]
        public decimal WorkdaysCompleted => WorkdayMath.FromPieces(PieceCount, PiecesPerWorkdayAtEntry);

        /// <summary>ملاحظات اختيارية (مثال: قطع بها عيوب، تأخير، ...إلخ)</summary>
        [MaxLength(300)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
        public virtual ProductionStage ProductionStage { get; set; } = null!;
    }
}
