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

    // ــــــــ الفهرس المغطّي ــــــــ
    // "الشغل الواقف" بيجمع كل قطعة اتسجّلت من أول يوم في البرنامج لحد
    // النهارده، مجمّعة بالمرحلة — وبيتنده من شاشة الإنتاج اليومي كل ما
    // المستخدم يختار منتج. يعني أكتر شاشة بتتفتح بتعمل استعلام بيكبر كل
    // يوم للأبد.
    //
    // على قاعدة بيانات بـ 30 سنة (432 ألف سجل) كان بياخد **1047 مللي**:
    // SQLite بيمشي على فهرس المرحلة وبيرجع للجدول لكل صف عشان يجيب
    // PieceCount. الفهرس ده فيه الأربع أعمدة اللي الاستعلام محتاجها،
    // فبيتحسب من الفهرس نفسه من غير ما يلمس الجدول — **40 مللي**.
    //
    // الترتيب مقصود: المرحلة أولًا (هي اللي بيتجمّع عليها)، وبعدها
    // التاريخ (الفلتر)، وبعدها الأعمدة الباقية عشان التغطية تكتمل.
    // أي تغيير في ترتيبهم بيرجّع الاستعلام لمسح الجدول.
    //
    // IsRework آخر عمود عن قصد: استعلامات الرجوع للحساب القديم في
    // ProductionStageOutputService بتفلتر عليه (سجلات الإعادة مش إنتاج)،
    // ولو مش جوّه الفهرس ده الفلتر هيرجّع SQLite للجدول صف صف — يعني
    // نفس الـ1047 مللي اللي الفهرس اتعمل أصلًا عشانها.
    [Index(nameof(ProductionStageId), nameof(Date), nameof(IsDeleted), nameof(PieceCount), nameof(IsRework))]
    public class DailyProduction : SoftDeletableEntity
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

        /// <summary>
        /// إعادة عمل: العامل رجع صلّح شغل خلص خلاص على المرحلة دي — مش
        /// إنتاج جديد خارج من الخط.
        ///
        /// السجل ده بيتحسب في يومية العامل وأجره **زي أي سجل تاني** —
        /// هو اشتغل فعلاً. اللي بيتغيّر إنه مايعدّش في الإنتاج الفعلي:
        /// ProductionStageOutputService بيتجاهله في كل استعلامات الرجوع
        /// للحساب القديم، فمايظهرش في "خلص كام" ولا في الشغل الواقف.
        ///
        /// الإنتاج الفعلي نفسه بيتسجّل من نطاقات الرحلة لوحدها
        /// (ProductionFlowService.RecordFlowAsync)، فسجل الإعادة أصلاً
        /// عمره ما بيضيف عليه حاجة.
        /// </summary>
        public bool IsRework { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // ------- العلاقات -------

        public virtual Worker Worker { get; set; } = null!;
        public virtual ProductionStage ProductionStage { get; set; } = null!;
    }
}
