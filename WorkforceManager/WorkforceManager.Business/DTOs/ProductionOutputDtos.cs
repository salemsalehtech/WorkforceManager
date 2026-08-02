namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// القطع المستنية قبل مرحلة معينة.
    ///
    /// مش مخزّنة في أي جدول — بتتحسب طرح: كل اللي خلص المرحلة اللي قبلها
    /// ناقص كل اللي خلص المرحلة دي، من أول التسجيل لحد اليوم المطلوب.
    /// </summary>
    public class StageWipDto
    {
        public int StageId { get; init; }
        public string StageName { get; init; } = string.Empty;

        /// <summary>ترتيب المرحلة في الخط (1 = أول مرحلة)</summary>
        public int StageOrder { get; init; }

        /// <summary>عدد القطع المستنية تشتغل على المرحلة دي</summary>
        public int WaitingPieces { get; init; }

        /// <summary>
        /// المرحلة دي اتسجل عليها أكتر من اللي قبلها — مستحيل واقعيًا،
        /// فمعناه غلط إدخال. بنقوله صريح بدل ما نصفّر الرقم في صمت.
        /// </summary>
        public bool IsOverCounted { get; init; }

        /// <summary>الزيادة المستحيلة (بتظهر في رسالة التنبيه)</summary>
        public int OverCountedBy { get; init; }
    }

    /// <summary>سطر واحد في تقرير الإنتاج اليومي — منتج واحد</summary>
    public class DailyProductReportDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>قطع خلصت آخر مرحلة في اليوم ده = المنتج التام</summary>
        public int CompletedPieces { get; init; }

        /// <summary>قطع دخلت أول مرحلة في اليوم ده</summary>
        public int StartedPieces { get; init; }

        /// <summary>الواقف عند كل مرحلة بنهاية اليوم (المراحل الفاضية مش بتظهر)</summary>
        public List<StageWipDto> StageWip { get; init; } = new();

        /// <summary>إجمالي الواقف في خط المنتج ده = اللي دخل الخط ناقص اللي خلصه</summary>
        public int ParkedPieces => StageWip.Sum(w => w.WaitingPieces);

        public bool HasParkedPieces => ParkedPieces > 0;

        /// <summary>فيه مرحلة اتسجل عليها أكتر من اللي قبلها — لازم يراجع</summary>
        public bool HasOverCounting => StageWip.Any(w => w.IsOverCounted);

        /// <summary>فيه حركة على المنتج ده النهارده؟ (لإخفاء المنتجات الساكنة)</summary>
        public bool HasActivity => CompletedPieces > 0 || StartedPieces > 0 || ParkedPieces > 0;
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
        public bool HasOverCounting => Products.Any(p => p.HasOverCounting);
    }

    /// <summary>ملخص ما قبل إقفال اليوم — المستخدم بيراجعه قبل ما يوافق</summary>
    public class DayClosurePreviewDto
    {
        public DateTime Date { get; init; }
        public bool AlreadyClosed { get; init; }

        /// <summary>قطع خلصت الخط النهارده</summary>
        public int CompletedPieces { get; init; }

        /// <summary>الواقف في المصنع كله بنهاية اليوم</summary>
        public int ParkedPieces { get; init; }

        /// <summary>الواقف مفصّل لكل منتج — المستخدم بيراجعه قبل ما يقفل</summary>
        public List<ParkedProductDto> ParkedByProduct { get; init; } = new();

        /// <summary>فيه أرقام مش منطقية لازم يشوفها قبل ما يقفل اليوم</summary>
        public bool HasOverCounting { get; init; }
    }

    /// <summary>الواقف في منتج واحد (لشاشة الإقفال اللي بتعرض المصنع كله)</summary>
    public class ParkedProductDto
    {
        public string ProductName { get; init; } = string.Empty;
        public int ParkedPieces { get; init; }

        /// <summary>أكتر مرحلة متكدّس عندها شغل — دي اللي محتاجة عمال بكرة</summary>
        public string BiggestQueueStage { get; init; } = string.Empty;
        public int BiggestQueuePieces { get; init; }
    }
}
