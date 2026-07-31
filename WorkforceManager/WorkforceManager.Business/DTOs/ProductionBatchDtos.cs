using WorkforceManager.Core.Enums;

namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// دفعة واقفة في الخط، جاهزة للعرض على المستخدم عشان يختار يكمّلها.
    /// </summary>
    public class OpenBatchDto
    {
        public int BatchId { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>عدد القطع الواقفة في الدفعة دي</summary>
        public int Quantity { get; init; }

        public DateTime StartedDate { get; init; }

        /// <summary>آخر مرحلة عدّتها (فاضي = لسه ما دخلتش الخط)</summary>
        public int? LastCompletedStageId { get; init; }
        public string LastCompletedStageName { get; init; } = string.Empty;

        /// <summary>ترتيب أول مرحلة المفروض تكمّل منها — النطاق لازم يبدأ من هنا</summary>
        public int NextStageOrder { get; init; }
        public int NextStageId { get; init; }
        public string NextStageName { get; init; } = string.Empty;

        /// <summary>عدّت كام مرحلة من كام (للعرض: "5 من 11")</summary>
        public int CompletedStages { get; init; }
        public int TotalStages { get; init; }

        /// <summary>الدفعة واقفة من كام يوم (0 = بدأت النهارده)</summary>
        public int DaysWaiting { get; init; }

        public string ProgressText => $"{CompletedStages} من {TotalStages} مرحلة";

        /// <summary>سطر الاختيار في القايمة: العدد + المرحلة الواقفة عندها + من إمتى</summary>
        public string PickerText =>
            $"{Quantity} قطعة — واقفة عند \"{NextStageName}\" ({ProgressText}) — من {StartedDate:dd/MM}";
    }

    /// <summary>
    /// نطاق إنتاج مربوط بدفعة. ده اللي بيتبعت للحفظ بدل
    /// <see cref="FlowRangeDto"/> بعد ما بقى الربط بالدفعة إجباري.
    /// </summary>
    public class BatchRangeDto
    {
        /// <summary>
        /// الدفعة اللي النطاق ده بيحرّكها. null = دفعة جديدة (النطاق لازم
        /// يبدأ من أول مرحلة في الخط، إلا لو <see cref="IsOpeningBalance"/>).
        /// </summary>
        public int? BatchId { get; init; }

        /// <summary>
        /// رصيد افتتاحي: القطع دي عدّت المراحل السابقة برّه النظام، فبنفتح
        /// لها دفعة جديدة **من نص الخط** بدل ما نرفض التسجيل.
        ///
        /// ده المخرج الوحيد من قاعدة "النطاق من نص الخط لازم يكمّل دفعة" —
        /// وهو مقصود إنه فعل صريح المستخدم بيختاره ويتسجّل في الدفعة، مش
        /// ثغرة صامتة. من غيره النظام مينفعش يشتغل أصلاً على مصنع شغّال
        /// فيه شغل واقف في الخط قبل ما نظام الدفعات يبدأ.
        /// </summary>
        public bool IsOpeningBalance { get; init; }

        public int FromStageId { get; init; }
        public int ToStageId { get; init; }

        /// <summary>عدد القطع اللي عدّت على كل مرحلة في النطاق</summary>
        public int PieceCount { get; init; }
    }

    /// <summary>ملخص حركة دفعة واحدة بعد الحفظ — للرسالة اللي بتظهر للمستخدم</summary>
    public class BatchMovementDto
    {
        public int BatchId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public int Pieces { get; init; }
        public BatchStatus Status { get; init; }

        /// <summary>اتقسمت: جزء كمّل وجزء فضل واقف</summary>
        public bool WasSplit { get; init; }
        public int LeftBehindPieces { get; init; }

        /// <summary>وقفت عند المرحلة دي (فاضي لو خلصت)</summary>
        public string StoppedAtStageName { get; init; } = string.Empty;

        public bool IsCompleted => Status == BatchStatus.Completed;
    }

    /// <summary>سطر واحد في تقرير الإنتاج اليومي — منتج واحد</summary>
    public class DailyProductReportDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>قطع خلصت الخط كله النهارده</summary>
        public int CompletedPieces { get; init; }

        /// <summary>
        /// من المكتمل ده، كام قطعة كانت مرحّلة من أيام قبل كده. الفرق بين
        /// ده والمكتمل = اللي بدأ وخلص في نفس اليوم.
        /// </summary>
        public int CompletedFromCarriedPieces { get; init; }

        /// <summary>بدأ وخلص النهارده</summary>
        public int CompletedSameDayPieces => CompletedPieces - CompletedFromCarriedPieces;

        /// <summary>الدفعات اللي لسه واقفة بنهاية اليوم، كل واحدة عند مرحلتها</summary>
        public List<ParkedLotDto> ParkedLots { get; init; } = new();

        public int ParkedPieces => ParkedLots.Sum(l => l.Quantity);
        public bool HasParkedLots => ParkedLots.Count > 0;

        /// <summary>فيه حركة على المنتج ده النهارده؟ (لإخفاء المنتجات الساكنة)</summary>
        public bool HasActivity => CompletedPieces > 0 || ParkedLots.Count > 0;
    }

    /// <summary>كمية واقفة عند مرحلة معينة</summary>
    public class ParkedLotDto
    {
        public int BatchId { get; init; }
        public int Quantity { get; init; }

        /// <summary>المرحلة اللي هتشتغل عليها بكرة (اللي بعد آخر مرحلة خلصتها)</summary>
        public string NextStageName { get; init; } = string.Empty;
        public int NextStageOrder { get; init; }

        public DateTime StartedDate { get; init; }
        public int DaysWaiting { get; init; }
    }

    /// <summary>تقرير الإنتاج اليومي كامل</summary>
    public class DailyProductionReportDto
    {
        public DateTime Date { get; init; }

        /// <summary>اليوم اتقفل؟ (مقفول = الأرقام دي نهائية)</summary>
        public bool IsClosed { get; init; }
        public DateTime? ClosedAt { get; init; }

        public List<DailyProductReportDto> Products { get; init; } = new();

        public int TotalCompletedPieces => Products.Sum(p => p.CompletedPieces);
        public int TotalParkedPieces => Products.Sum(p => p.ParkedPieces);
        public int TotalCarriedInPieces => Products.Sum(p => p.CompletedFromCarriedPieces);
    }

    /// <summary>ملخص ما قبل إقفال اليوم — المستخدم بيراجعه قبل ما يوافق</summary>
    public class DayClosurePreviewDto
    {
        public DateTime Date { get; init; }
        public bool AlreadyClosed { get; init; }

        /// <summary>الدفعات اللي هتتّرحّل لبكرة</summary>
        public List<ParkedLotWithProductDto> CarriedLots { get; init; } = new();

        public int CarriedBatchCount => CarriedLots.Count;
        public int CarriedPieces => CarriedLots.Sum(l => l.Quantity);

        /// <summary>قطع خلصت الخط النهارده (بتتقفل معاه)</summary>
        public int CompletedPieces { get; init; }
    }

    /// <summary>كمية واقفة + اسم منتجها (لشاشة الإقفال اللي بتعرض كل المصنع)</summary>
    public class ParkedLotWithProductDto
    {
        public int BatchId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public string NextStageName { get; init; } = string.Empty;
        public DateTime StartedDate { get; init; }
        public int DaysWaiting { get; init; }
    }
}
