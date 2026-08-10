namespace WorkforceManager.Business.DTOs
{
    /// <summary>سطر واحد في تقرير الإنتاج اليومي — منتج واحد</summary>
    public class DailyProductReportDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>
        /// المنتج **الصالح** اللي خرج النهارده = إنتاج آخر مرحلة ناقص
        /// هالكها. القطعة اللي خلصت الخط والجودة رفضتها مش منتج تام.
        /// </summary>
        public int CompletedPieces { get; init; }

        /// <summary>قطع دخلت أول مرحلة في اليوم ده</summary>
        public int StartedPieces { get; init; }

        /// <summary>
        /// كل الهالك المسجّل على المنتج ده النهارده — على أي مرحلة.
        ///
        /// منفصل عن <see cref="CompletedPieces"/> عن قصد: الهالك في نص
        /// الخط مبيأثرش على التام (القطع دي أصلاً ماوصلتش الآخر)، بس
        /// المستخدم عايز يشوفه.
        /// </summary>
        public int ScrapPieces { get; init; }

        /// <summary>فيه حركة على المنتج ده النهارده؟ (لإخفاء المنتجات الساكنة)</summary>
        public bool HasActivity => CompletedPieces > 0 || StartedPieces > 0 || ScrapPieces > 0;
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
        public int TotalStartedPieces => Products.Sum(p => p.StartedPieces);

        /// <summary>كل الهالك المسجّل النهارده على أي مرحلة</summary>
        public int TotalScrapPieces => Products.Sum(p => p.ScrapPieces);
    }

    /// <summary>ملخص ما قبل إقفال اليوم — المستخدم بيراجعه قبل ما يوافق</summary>
    public class DayClosurePreviewDto
    {
        public DateTime Date { get; init; }
        public bool AlreadyClosed { get; init; }

        /// <summary>قطع خلصت الخط النهارده</summary>
        public int CompletedPieces { get; init; }

        /// <summary>قطع دخلت الخط النهارده</summary>
        public int StartedPieces { get; init; }

        /// <summary>
        /// الهالك المسجّل النهارده. بيتعرض قبل القفل عشان المستخدم
        /// يراجعه وهو لسه يقدر يعدّله — بعد القفل مش هينفع يتسجّل عليه.
        /// </summary>
        public int ScrapPieces { get; init; }

        /// <summary>فيه هالك؟ (بيخفي السطر خالص لما ميكونش)</summary>
        public bool HasScrap => ScrapPieces > 0;

        /// <summary>إنتاج كل منتج في اليوم — المستخدم بيراجعه قبل ما يقفل</summary>
        public List<ProductOutputDto> ByProduct { get; init; } = new();
    }

    /// <summary>إنتاج منتج واحد في اليوم (لشاشة الإقفال اللي بتعرض المصنع كله)</summary>
    public class ProductOutputDto
    {
        public string ProductName { get; init; } = string.Empty;
        public int CompletedPieces { get; init; }
        public int StartedPieces { get; init; }
        public int ScrapPieces { get; init; }

        /// <summary>نص الهالك جنب المنتج — فاضي لما مفيش هالك</summary>
        public string ScrapText => ScrapPieces > 0 ? $"{ScrapPieces:N0} هالك" : "";
    }
}
