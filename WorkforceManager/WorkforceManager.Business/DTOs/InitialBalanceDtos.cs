using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>نطاق واحد داخل رصيد أولي — للعرض والإنشاء (شوف InitialBalanceRange)</summary>
    public class InitialBalanceRangeDto
    {
        public int Id { get; init; }
        public int FromStageId { get; init; }
        public string FromStageName { get; init; } = string.Empty;
        public int ToStageId { get; init; }
        public string ToStageName { get; init; } = string.Empty;
        public int PieceCount { get; init; }
        public int SortOrder { get; init; }

        /// <summary>كام قطعة اتاخدت فعلًا من النطاق ده (شوف InitialBalanceRangeMath.UsedQuantity) — للعرض/التعديل، عشان الشاشة تقفل الامتداد على نطاق عليه استخدام</summary>
        public int UsedQuantity { get; init; }
    }

    /// <summary>طلب إضافة نطاق جديد لرصيد قائم</summary>
    public class AddInitialBalanceRangeRequest
    {
        public int FromStageId { get; init; }
        public int ToStageId { get; init; }
        public int PieceCount { get; init; }
    }

    /// <summary>
    /// صف واحد في قايمة نطاقات معدّلة كاملة — لـ <see cref="WorkforceManager.Business.Services.InitialBalanceService.EditAsync"/>.
    /// <see cref="Id"/> = null معناه نطاق جديد (بيتحقق منه بـ From/To/PieceCount)؛
    /// <see cref="Id"/> معروف معناه نطاق موجود بيتحدّث عدد قطعه بس —
    /// From/To بتاعته بتتجاهل تمامًا (الامتداد مقفول بمجرد ما يتاخد منه أي حاجة،
    /// شوف تعليق EditAsync). أي نطاق موجود مش في القايمة دي بيتشال، بشرط
    /// مايكونش عليه استخدام.
    /// </summary>
    public class InitialBalanceRangeEditItem
    {
        public int? Id { get; init; }
        public int FromStageId { get; init; }
        public int ToStageId { get; init; }
        public int PieceCount { get; init; }
    }

    /// <summary>عملية استخدام/إكمال واحدة من رصيد أولي — للعرض في السجل/التاريخ</summary>
    public class InitialBalanceUsageDto
    {
        public int Id { get; init; }
        public DateTime UsedDate { get; init; }
        public int Quantity { get; init; }
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
        public int ProductionStageId { get; init; }
        public string StageName { get; init; } = string.Empty;
        public int? InitialBalanceRangeId { get; init; }
        public string? Notes { get; init; }
        public string? RecordedBy { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    /// <summary>رصيد أولي كامل بتفاصيله — لبطاقة/شاشة الرصيد الأولي لكل منتج</summary>
    public class InitialBalanceDto
    {
        public int Id { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Notes { get; init; }
        public int Quantity { get; init; }
        public int UsedQuantity { get; init; }
        public int RemainingQuantity { get; init; }
        public InitialBalanceStatus Status { get; init; }
        public DateTime OriginalDate { get; init; }
        public InitialBalanceSource Source { get; init; }
        public int? OriginalDailyProductionId { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? CreatedBy { get; init; }
        public List<InitialBalanceRangeDto> Ranges { get; init; } = new();

        /// <summary>عدد القطع من كمية النطاقات مش لسه متخصص لنطاق معين (يفضل قابل للاستخدام من غير نطاق محدد)</summary>
        public int UnrangedQuantity => Quantity - Ranges.Sum(r => r.PieceCount);
    }

    /// <summary>طلب إنشاء رصيد أولي يدويًا أو من قطع ناقصة برحلة إنتاج</summary>
    public class CreateInitialBalanceRequest
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Notes { get; init; }
        public int Quantity { get; init; }
        public DateTime OriginalDate { get; init; }
        public InitialBalanceSource Source { get; init; } = InitialBalanceSource.Manual;
        public int? OriginalDailyProductionId { get; init; }
        public List<AddInitialBalanceRangeRequest> Ranges { get; init; } = new();
    }

    /// <summary>سحب كمية معيّنة من نطاق معيّن — جزء من طلب InitialBalanceService.WithdrawAsync</summary>
    public class InitialBalanceRangeWithdrawalDto
    {
        public int RangeId { get; init; }
        public int PieceCount { get; init; }
    }

    /// <summary>
    /// تجميع كل الأرصدة الأولية النشطة لمنتج واحد في رقم واحد — للكارت
    /// المُجمّع في شاشة الإنتاج اليومي (عرض بصري بحت؛ البيانات تحتيه
    /// تفضل مقسّمة بالرصيد/النطاق زي ما هي فعليًا، شوف InitialBalanceService.GetForProductAsync)
    /// </summary>
    public class InitialBalanceSummaryDto
    {
        public int ProductId { get; init; }
        public int TotalQuantity { get; init; }
        public int UsedQuantity { get; init; }
        public int RemainingQuantity { get; init; }

        /// <summary>عدد الأرصدة اللي لسه فيها متاح (Available أو PartiallyUsed)</summary>
        public int ActiveBalanceCount { get; init; }
    }
}
