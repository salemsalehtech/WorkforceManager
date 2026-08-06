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

        /// <summary>
        /// القطع اللي خلصت **آخر مرحلة** في الفترة = المنتج التام.
        ///
        /// ده الرقم اللي معناه "أنتجنا كام من المنتج ده". جمع القطع على
        /// كل المراحل غلط فادح: القطعة الواحدة بتعدّي 11 مرحلة، فـ 5,000
        /// قطعة بتتحسب 55,000. القطعة بتعدّي المراحل بالترتيب مش
        /// بالتوازي، فمجموع المراحل مش بيقيس أي حاجة.
        /// </summary>
        public int CompletedPieces { get; init; }

        /// <summary>القطع اللي دخلت **أول مرحلة** في الفترة</summary>
        public int StartedPieces { get; init; }

        /// <summary>
        /// مجموع القطع على كل المراحل — **مقياس شغل مش مقياس إنتاج**.
        ///
        /// موجود عشان "اتشغل على المنتج ده ولا لأ" بس (<see cref="WorkedInPeriod"/>)
        /// وعشان يوميات العمال. **ممنوع يتعرض للمستخدم كإنتاج**.
        /// </summary>
        public int StageWorkPieces { get; init; }

        /// <summary>العمال اللي سجّلوا شغل عليه في الفترة</summary>
        public IReadOnlySet<int> WorkerIds { get; init; } = new HashSet<int>();

        /// <summary>مراحله النشطة (لفلترة "المنتجات اللي فيها المرحلة دي")</summary>
        public IReadOnlySet<int> StageIds { get; init; } = new HashSet<int>();

        /// <summary>عدد الأيام اللي اتسجل فيها شغل عليه</summary>
        public int DaysWorked { get; init; }

        /// <summary>
        /// اشتغل عليه فعلًا في الفترة؟ ده معنى "شغّال" الجديد — مش فلاج
        /// <see cref="IsActive"/> اللي بيفضل مفعّل حتى لو المنتج متسيب.
        ///
        /// بيعتمد على أي شغل على أي مرحلة مش على التام: منتج اتشتغل عليه
        /// كل الأسبوع ولسه ماوصلش لآخر الخط هو "شغّال" فعلاً.
        /// </summary>
        public bool WorkedInPeriod => StageWorkPieces > 0;
    }
}
