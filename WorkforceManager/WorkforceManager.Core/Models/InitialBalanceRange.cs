using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// نطاق واحد داخل رصيد أولي: جزء من كمية الرصيد مخصّص لمجموعة مراحل
    /// معينة (من مرحلة X لمرحلة Y)، بالظبط زي
    /// <see cref="WorkforceManager.Business.DTOs.FlowRangeDto"/> في رحلة
    /// الإنتاج العادية — بس هنا محفوظ كصف عشان يفضل مرتبط بالرصيد لحد
    /// ما يتاخد.
    ///
    /// **المستخدم هو اللي بيحدد المراحل يدويًا** — النظام مايفترضش إن
    /// "من مرحلة X لمرحلة Y" معناه كل مرحلة بينهم (نفس مبدأ نطاقات
    /// رحلة الإنتاج العادية).
    ///
    /// مش كل كمية الرصيد لازم يكون ليها نطاق: الباقي بعد مجموع النطاقات
    /// يفضل من غير نطاق محدد لحد ما يتحدد وقت الاستخدام.
    /// </summary>
    public class InitialBalanceRange
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(InitialBalance))]
        public int InitialBalanceId { get; set; }

        [Required]
        [ForeignKey(nameof(FromStage))]
        public int FromStageId { get; set; }

        [Required]
        [ForeignKey(nameof(ToStage))]
        public int ToStageId { get; set; }

        /// <summary>عدد القطع من كمية الرصيد المخصصة لهذا النطاق تحديدًا</summary>
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "عدد قطع النطاق يجب أن يكون رقمًا موجبًا")]
        public int PieceCount { get; set; }

        /// <summary>ترتيب عرض النطاقات داخل الرصيد</summary>
        public int SortOrder { get; set; }

        // ------- العلاقات -------

        public virtual InitialBalance InitialBalance { get; set; } = null!;

        public virtual ProductionStage FromStage { get; set; } = null!;

        public virtual ProductionStage ToStage { get; set; } = null!;
    }
}
