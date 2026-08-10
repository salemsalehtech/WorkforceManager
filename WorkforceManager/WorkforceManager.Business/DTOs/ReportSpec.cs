namespace WorkforceManager.Business.DTOs
{
    /// <summary>التقرير عن إيه</summary>
    public enum ReportSubject
    {
        Production = 1,
        Attendance = 2,
        Wages = 3,
        Penalties = 4,
        WageAdjustments = 5,
        Skills = 6
    }

    /// <summary>الصفوف بتتجمّع على إيه</summary>
    public enum ReportGrouping
    {
        Worker = 1,
        Product = 2,
        Stage = 3,
        Day = 4,
        Week = 5
    }

    /// <summary>نوع العامل — فلتر اختياري</summary>
    public enum WorkerKindFilter
    {
        All = 0,
        ByProduction = 1,
        Hourly = 2
    }

    /// <summary>
    /// وصف تقرير كامل: عن إيه، لمدة إيه، متجمّع إزاي، ومفلتر بإيه.
    ///
    /// الوصف ده هو **كل** اللي بيفرّق بين تقرير وتقرير في البرنامج.
    /// الأربع تقارير اللي كانت مكتوبة بإيدها (كشف الأسبوع، كشف الأجور،
    /// التقرير العام، تقرير العامل) هي أربع نسخ من الكلاس ده بقيم
    /// مختلفة — عشان كده بقت قوالب بدل ما تبقى أربع شاشات.
    ///
    /// الكلاس ده كمان هو اللي بيتحفظ لما المستخدم يحفظ قالب: أربع
    /// اختيارات في ملف صغير، مش استعلام ولا كود.
    /// </summary>
    public class ReportSpec
    {
        public ReportSubject Subject { get; init; } = ReportSubject.Production;
        public ReportGrouping GroupBy { get; init; } = ReportGrouping.Worker;

        public DateTime From { get; init; } = DateTime.Today;
        public DateTime To { get; init; } = DateTime.Today;

        // ------- الفلاتر (كلها اختيارية) -------
        // null أو فاضي = الفلتر مقفول. **مش** "طابق الفاضي" — نفس قاعدة
        // WorkerFilterRules في شاشة العمال.

        /// <summary>عمال معيّنين بس</summary>
        public IReadOnlyCollection<int>? WorkerIds { get; init; }

        /// <summary>منتجات معيّنة بس</summary>
        public IReadOnlyCollection<int>? ProductIds { get; init; }

        /// <summary>مراحل معيّنة بس</summary>
        public IReadOnlyCollection<int>? StageIds { get; init; }

        public WorkerKindFilter WorkerKind { get; init; } = WorkerKindFilter.All;

        /// <summary>
        /// يزوّد عمود بيقارن كل رقم بنفس المدة اللي قبلها.
        ///
        /// المدة السابقة بتتحسب بنفس الطول بالظبط: شهر بيتقارن بشهر،
        /// وأسبوع بأسبوع — مش بشهر تقويمي أو أسبوع تقويمي، عشان
        /// المقارنة تفضل عادلة مهما كانت المدة المختارة.
        /// </summary>
        public bool CompareWithPrevious { get; init; }

        // ------- شكل الجدول -------

        /// <summary>
        /// أعمدة المستخدم: مين يظهر، بأي ترتيب، وباسم إيه.
        /// null = الأعمدة الافتراضية للموضوع كلها بترتيبها.
        /// </summary>
        public IReadOnlyList<ReportColumnChoice>? ColumnLayout { get; init; }

        /// <summary>
        /// مفتاح العمود اللي بيترتب بيه (<see cref="ReportTable.LabelColumnKey"/>
        /// للاسم). null = الترتيب الطبيعي للموضوع.
        /// </summary>
        public string? SortKey { get; init; }

        public bool SortDescending { get; init; } = true;

        /// <summary>أعلى/أقل N صف بس. null = كل الصفوف.</summary>
        public int? TopN { get; init; }

        /// <summary>المدة اللي قبل المختارة بنفس طولها بالظبط</summary>
        public (DateTime From, DateTime To) PreviousPeriod()
        {
            var days = (To.Date - From.Date).Days + 1;
            return (From.Date.AddDays(-days), From.Date.AddDays(-1));
        }

        // ------- إيه اللي ينفع مع إيه -------

        /// <summary>
        /// التجميعات اللي ليها معنى مع كل موضوع.
        ///
        /// مش كل تركيبة ليها معنى: "الحضور بالمنتج" سؤال مالوش إجابة —
        /// الحضور بيتسجّل على العامل في اليوم، مالوش علاقة بمنتج. الجدول
        /// ده بيخلي الشاشة تعرض الاختيارات المفيدة بس بدل ما المستخدم
        /// يوصل لتقرير فاضي ويفتكر إن فيه عطل.
        /// </summary>
        public static IReadOnlyList<ReportGrouping> AllowedGroupings(ReportSubject subject) =>
            subject switch
            {
                ReportSubject.Production => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Product,
                    ReportGrouping.Stage, ReportGrouping.Day, ReportGrouping.Week
                },
                ReportSubject.Attendance => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Day, ReportGrouping.Week
                },
                // الأجر بيتحسب على العامل في مدة — تقسيمه بالمنتج مالوش
                // معنى لأن العامل بيتحاسب على مجموع شغله مش على منتج
                ReportSubject.Wages => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Week
                },
                ReportSubject.Penalties => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Day, ReportGrouping.Week
                },
                ReportSubject.WageAdjustments => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Day, ReportGrouping.Week
                },
                // المهارات حالة مش مدة — المدة مالهاش أي تأثير عليها
                ReportSubject.Skills => new[]
                {
                    ReportGrouping.Worker, ReportGrouping.Product, ReportGrouping.Stage
                },
                _ => new[] { ReportGrouping.Worker }
            };

        public static bool IsAllowed(ReportSubject subject, ReportGrouping grouping) =>
            AllowedGroupings(subject).Contains(grouping);

        /// <summary>
        /// المهارات حالة حالية مش حركة في مدة، فالتاريخ مش بيأثر فيها.
        /// الشاشة بتخفي اختيار المدة عشان متوعدش بحاجة مش بتحصل.
        /// </summary>
        public static bool UsesPeriod(ReportSubject subject) =>
            subject != ReportSubject.Skills;

        public static string SubjectName(ReportSubject subject) => subject switch
        {
            ReportSubject.Production => "الإنتاج",
            ReportSubject.Attendance => "الحضور والغياب",
            ReportSubject.Wages => "الأجور",
            ReportSubject.Penalties => "الجزاءات",
            ReportSubject.WageAdjustments => "السلف والحوافز",
            ReportSubject.Skills => "المهارات",
            _ => "تقرير"
        };

        public static string GroupingName(ReportGrouping grouping) => grouping switch
        {
            ReportGrouping.Worker => "بالعامل",
            ReportGrouping.Product => "بالمنتج",
            ReportGrouping.Stage => "بالمرحلة",
            ReportGrouping.Day => "باليوم",
            ReportGrouping.Week => "بالأسبوع",
            _ => ""
        };
    }
}
