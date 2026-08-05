namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// نشاط منتج واحد في فترة — الأساس اللي شاشة المنتجات بتفلتر
    /// وتحسب إحصائياتها منه.
    /// </summary>
    public class ProductActivityDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>الفلاج في قاعدة البيانات: مسموح يشتغل عليه ولا موقوف</summary>
        public bool IsActive { get; init; }

        /// <summary>مجموع القطع المسجّلة على كل مراحله في الفترة</summary>
        public int PiecesProduced { get; init; }

        /// <summary>العمال اللي سجّلوا شغل عليه في الفترة</summary>
        public IReadOnlySet<int> WorkerIds { get; init; } = new HashSet<int>();

        /// <summary>مراحله النشطة (لفلترة "المنتجات اللي فيها المرحلة دي")</summary>
        public IReadOnlySet<int> StageIds { get; init; } = new HashSet<int>();

        /// <summary>عدد الأيام اللي اتسجل فيها شغل عليه</summary>
        public int DaysWorked { get; init; }

        /// <summary>
        /// اشتغل عليه فعلًا في الفترة؟ ده معنى "شغّال" الجديد — مش فلاج
        /// <see cref="IsActive"/> اللي بيفضل مفعّل حتى لو المنتج متسيب.
        /// </summary>
        public bool WorkedInPeriod => PiecesProduced > 0;
    }
}
