namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// النيات المدعومة في "بحث سريع": عبارة قصيرة "كلمة نية + اسم" بتجاوب
    /// برقم محسوب فورًا، بدل ما تودّي المستخدم لشاشة يستنتج منها الرقم
    /// بنفسه. شوف <see cref="Services.SearchIntentService"/> للتفاصيل.
    /// </summary>
    public enum SearchIntentKind
    {
        Absence,
        Penalties,
        Adjustments,
        Wage,
        Skills,
        Production,
        AverageProduction,
        Standing,

        /// <summary>"إنتاج يوم [مرجع يوم]" — بلا اسم، إجمالي المصنع في يوم واحد بعينه</summary>
        DayProduction,

        /// <summary>"غياب النهارده"/"غياب الأسبوع ده" — بلا اسم، مين غايب في المصنع كله</summary>
        DayAbsence,

        /// <summary>"أعلى إنتاج الأسبوع ده" — بلا اسم، أعلى منتج وأعلى عامل إنتاجًا في الفترة</summary>
        TopProduction,

        /// <summary>"أقل إنتاج الشهر ده" — نفس TopProduction بس أقل بدل أعلى</summary>
        BottomProduction,

        /// <summary>"مين أحسن عامل الأسبوع ده" — بلا اسم، ترتيب الفريق كله</summary>
        TopWorker,

        /// <summary>"أسوأ عامل" — نفس TopWorker بس آخر الترتيب بدل أوله</summary>
        BottomWorker,

        /// <summary>"مين شغال على [منتج أو مرحلة]" — العمال المؤهلين، مرتبين بالنجوم</summary>
        ProductWorkers
    }

    /// <summary>ناتج تفكيك عبارة البحث لنية + اسم مرشّح — منطق نقي، مفيش قاعدة بيانات هنا</summary>
    public class ParsedIntentQuery
    {
        public required SearchIntentKind Kind { get; init; }

        /// <summary>باقي العبارة بعد شيل كلمة/كلمات النية والفترة — لسه مش متأكد إنه اسم حقيقي</summary>
        public required string CandidateName { get; init; }

        /// <summary>null = مفيش كلمة فترة في العبارة، الافتراضي (أسبوع العمل الحالي عادةً) بيتحدد وقت البناء</summary>
        public Services.ReportPeriodKind? Period { get; init; }

        /// <summary>لـ DayProduction بس — التاريخ الفعلي المحسوب من مرجع اليوم (الثلاثاء اللي فات، امبارح...)</summary>
        public DateTime? SpecificDay { get; init; }

        /// <summary>
        /// لـ TopWorker/BottomWorker بس — العبارة فيها "فات" ("الأسبوع اللي
        /// فات")، يعني الفترة قبل الحالية مش الحالية. مفهوم محلي هنا فقط،
        /// شوف SearchIntentService لتفسير ليه مش في ReportPeriodKind المشترك.
        /// </summary>
        public bool IsPastPeriod { get; init; }
    }

    /// <summary>سطر واحد في بطاقة الإجابة (تسمية: قيمة) — كلاس بسيط بدل Tuple عشان يتربط في XAML بوضوح</summary>
    public class SearchIntentAnswerLine
    {
        public required string Label { get; init; }
        public required string Value { get; init; }
    }

    /// <summary>
    /// الإجابة المحسوبة الجاهزة للعرض جوه نتيجة البحث. <see cref="Lines"/> فاضية
    /// و<see cref="EmptyNote"/> موجودة يعني "الاسم اتطابق والنية اتفهمت، بس مفيش
    /// بيانات فعلية للفترة دي" — حالة لازم تتقال بوضوح، مش تتسكت.
    /// </summary>
    public class SearchIntentAnswer
    {
        public required SearchIntentKind Kind { get; init; }

        /// <summary>عنوان البطاقة الكامل للعرض (بالنية والفترة) — مش اسم صالح لصندوق بحث شاشة تانية، شوف Name</summary>
        public required string Title { get; init; }

        /// <summary>
        /// اسم العامل/المنتج المطابَق وحده (بدون النية ولا الفترة) — ده
        /// اللي بيتحط في SearchText عند الهبوط على شاشة العامل/المنتج،
        /// نفس اللي فئتي Worker/Product العاديتين بتحطاه من PrimaryText بتاعهم.
        /// null للنيات اللي بلا اسم أصلًا (DayProduction، DayAbsence) —
        /// مفيش عامل/منتج واحد تهبط عليه، فمفيش SearchText لازمة.
        /// </summary>
        public string? Name { get; init; }

        public List<SearchIntentAnswerLine> Lines { get; init; } = new();
        public string? EmptyNote { get; init; }
        public int? WorkerId { get; init; }
        public int? ProductId { get; init; }
    }
}
