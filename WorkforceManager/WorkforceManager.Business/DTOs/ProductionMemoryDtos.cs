namespace WorkforceManager.Business.DTOs
{
    /// <summary>خطة إنتاج متأجّلة كما تُعرض في شاشة الذاكرة أو نافذة التذكير</summary>
    public class ProductionMemoryDto
    {
        public int Id { get; init; }

        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        public string Notes { get; init; } = string.Empty;

        public DateTime RemindOn { get; init; }

        /// <summary>null = لسه نشطة</summary>
        public DateTime? CompletedAt { get; init; }

        /// <summary>الترتيب المخطط له، بترتيبه</summary>
        public IReadOnlyList<ProductionMemoryStageDto> Stages { get; init; } =
            Array.Empty<ProductionMemoryStageDto>();

        /// <summary>
        /// سبب تعطيل "ابدأ الآن" — null معناها الخطة صالحة للتشغيل.
        ///
        /// **مشتق وقت القراءة، مش مخزّن**: المنتج ممكن يتشال أو يتوقف
        /// أو مراحله تتغيّر في أي وقت بعد ما الخطة اتكتبت، فالتخزين
        /// هيبقى لقطة بايتة. التذكير بيتعرض في الحالتين — اللي بيتغيّر
        /// هو إن الزرار بيتعطّل والسبب بيتقال.
        /// </summary>
        public string? BlockedReason { get; init; }

        public bool CanStart => BlockedReason is null;
    }

    /// <summary>مرحلة واحدة في خطة، بموقعها المخطط</summary>
    public class ProductionMemoryStageDto
    {
        public int ProductionStageId { get; init; }
        public string StageName { get; init; } = string.Empty;

        /// <summary>موقعها في الخطة (من 1 للعرض)</summary>
        public int Position { get; init; }

        /// <summary>
        /// المرحلة دي لسه في خط إنتاج المنتج النشط؟ لو لأ، الخطة بايتة
        /// جزئيًا والعرض بيوضّح ده بدل ما يخفيه.
        /// </summary>
        public bool IsStillInLine { get; init; }
    }
}
