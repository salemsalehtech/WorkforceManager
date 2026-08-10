namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// أي أحداث سجل العمليات ينفع تتمسح بدري وأيها لازم يعيش أطول.
    ///
    /// السجل ده مالوش صف واحد "روتيني" بالمعنى المعتاد — كل حدث فيه إما
    /// حذف أو حركة فلوس. فالتقسيم مش بين "مهم ومش مهم"، هو بين:
    ///   • حدث بتحتاجه وانت بتراجع الأسبوع اللي فات (مين مسح اليوم ده؟)
    ///   • وحدث ممكن حد يسألك عنه بعد شهور (ليه خصمت مني في أغسطس؟)
    /// التاني ده هو اللي بيعيش سنة.
    ///
    /// **القاعدة معكوسة عن قصد**: بنعدّد اللي *ينفع* يتمسح بدري، فأي نوع
    /// حدث جديد يتضاف بعدين بياخد المدة الطويلة تلقائيًا. لو كانت
    /// معدودة بالعكس، نوع جديد بخصوص فلوس كان هيتمسح بعد ٩٠ يوم لمجرد
    /// إن حد نسي يضيفه للقايمة.
    /// </summary>
    public static class ActivityEventRetention
    {
        /// <summary>
        /// أحداث بتخص "إيه اللي حصل في المصنع" مش "مين وياخد كام":
        /// الحذف الإداري، والحفظ اليومي المتكرر.
        ///
        /// **الحفظ اليومي هو النوع الوحيد الروتيني في السجل**، واتحط هنا
        /// عن قصد: تسجيل الإنتاج والحضور بيحصلوا كل يوم على كل منتج،
        /// فسنة منهم بتغرق السجل وتخفي الحاجات اللي فعلًا بيتسأل عنها.
        /// تلات شهور كفاية للسؤال اللي بيتسأل عليهم ("مين حفظ اليوم ده؟").
        /// </summary>
        private static readonly HashSet<ActivityEventType> ShortLived = new()
        {
            ActivityEventType.ProductionDayDeleted,
            ActivityEventType.ProductionRecordDeleted,
            ActivityEventType.WorkerDeleted,
            ActivityEventType.ProductDeleted,
            ActivityEventType.StageDeleted,

            // الروتيني: بيتكرر كل يوم
            ActivityEventType.ProductionRecorded,
            ActivityEventType.AttendanceSaved,
            ActivityEventType.ProductionDayClosed,
            ActivityEventType.ProductionDayReopened,

            // الإضافات: بتتعمل مرة والنتيجة نفسها باينة في الشاشات
            ActivityEventType.WorkerCreated,
            ActivityEventType.ProductCreated,
            ActivityEventType.StageCreated
        };

        /// <summary>ينفع يتمسح بمدة الاحتفاظ القصيرة</summary>
        public static bool IsShortLived(ActivityEventType type) => ShortLived.Contains(type);

        /// <summary>
        /// لازم يعيش بالمدة الطويلة: تغيير أجر، تصحيح قطع، جزاء، سلفة أو
        /// حافز، وتغيير كلمة سر العمليات (دي البوابة اللي بتحمي كل اللي
        /// فوق، فمعرفة مين غيّرها جزء من نفس السؤال).
        /// </summary>
        public static IReadOnlyCollection<ActivityEventType> LongLivedTypes =>
            Enum.GetValues<ActivityEventType>().Where(t => !IsShortLived(t)).ToList();

        public static IReadOnlyCollection<ActivityEventType> ShortLivedTypes => ShortLived;
    }
}
