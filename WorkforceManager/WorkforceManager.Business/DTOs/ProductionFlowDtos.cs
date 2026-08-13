namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// نطاق إنتاج واحد في رحلة الإنتاج: "من المرحلة كذا إلى المرحلة كذا
    /// تم إنتاج عدد معين" — كل مرحلة داخل النطاق بتاخد نفس عدد القطع،
    /// لأن القطعة اللي وصلت لآخر مرحلة في النطاق تكون عدّت على كل
    /// المراحل اللي قبلها فيه. الترتيب بيتحدد بترتيب المراحل في المنتج
    /// (SortOrder) مش بترتيب عشوائي.
    /// </summary>
    public class FlowRangeDto
    {
        /// <summary>أول مرحلة في النطاق (بترتيب خط الإنتاج)</summary>
        public int FromStageId { get; init; }

        /// <summary>آخر مرحلة في النطاق (ممكن تكون نفس الأولى = نطاق من مرحلة واحدة)</summary>
        public int ToStageId { get; init; }

        /// <summary>عدد القطع اللي عدّت على كل مرحلة من مراحل النطاق</summary>
        public int PieceCount { get; init; }
    }

    /// <summary>
    /// نصيب عامل واحد من قطع مرحلة معينة في رحلة الإنتاج. المرحلة
    /// الواحدة ممكن يشتغل عليها أكتر من عامل في نفس اليوم، وساعتها
    /// مجموع أنصبتهم لازم يساوي إنتاج المرحلة الكامل (بيتحقق منه الحفظ).
    /// </summary>
    public class FlowShareDto
    {
        public int ProductionStageId { get; init; }
        public int WorkerId { get; init; }

        /// <summary>عدد القطع اللي أنتجها هذا العامل تحديدًا في هذه المرحلة</summary>
        public int PieceCount { get; init; }
    }

    /// <summary>
    /// عامل رص/تدريب متحط على مرحلة لليوم بس — مجرد تسجيل "كان هنا
    /// النهارده"، مالوش قطع ومالوش شرط تأهيل (WorkerSkill). بيتسجّل
    /// حاضر بيومية شيفت عادي (1) تلقائيًا، منفصل تمامًا عن
    /// <see cref="FlowShareDto"/>.
    /// </summary>
    public class FlowTaggedWorkerDto
    {
        public int ProductionStageId { get; init; }
        public int WorkerId { get; init; }
    }

    /// <summary>نتيجة حفظ رحلة إنتاج — للملخص المعروض للمستخدم بعد الحفظ</summary>
    public class FlowSaveResultDto
    {
        /// <summary>عدد سجلات الإنتاج اللي اتسجلت (سجل لكل عامل/مرحلة)</summary>
        public int RecordsCount { get; init; }

        /// <summary>عدد المراحل اللي اتسجل عليها إنتاج في الرحلة</summary>
        public int StagesCovered { get; init; }

        /// <summary>عدد العمال اللي اتسجل لهم حضور تلقائي (اللي ماكانش لهم سجل حضور في اليوم)</summary>
        public int AttendanceMarkedCount { get; init; }

        /// <summary>إجمالي كل عامل شارك في الرحلة (قطعه ويومياته) — مرتب بالأعلى يوميات</summary>
        public List<FlowWorkerTotalDto> WorkerTotals { get; init; } = new();

        /// <summary>قطع خلصت آخر مرحلة في الحفظة دي = منتج تام</summary>
        public int CompletedPieces { get; init; }

        /// <summary>قطع دخلت أول مرحلة في الحفظة دي</summary>
        public int StartedPieces { get; init; }
    }

    /// <summary>إجمالي عامل واحد في رحلة الإنتاج (لملخص ما بعد الحفظ والمعاينة قبل الحفظ)</summary>
    public class FlowWorkerTotalDto
    {
        public string WorkerName { get; init; } = string.Empty;
        public int TotalPieces { get; init; }
        public decimal TotalWorkdays { get; init; }
    }

    /// <summary>
    /// توزيع عمال آخر يوم اتسجل فيه إنتاج على منتج معين — أساس زرار
    /// "كرّر إدخال يوم فات". بيرجّع مين اشتغل على كل مرحلة **من غير
    /// أعداد القطع** عن قصد: الأعداد بتختلف كل يوم، ونسخها بيخاطر إن
    /// المستخدم يحفظ رقم إمبارح من غير ما ياخد باله.
    /// </summary>
    public class LastFlowDto
    {
        /// <summary>اليوم اللي التوزيع ده اتاخد منه (بيتعرض للمستخدم قبل ما يأكد)</summary>
        public DateTime Date { get; init; }

        /// <summary>أزواج (مرحلة، عامل) زي ما كانت في اليوم ده</summary>
        public List<FlowAssignmentDto> Assignments { get; init; } = new();
    }

    /// <summary>عامل واحد كان متوزع على مرحلة واحدة (من غير عدد قطع)</summary>
    public class FlowAssignmentDto
    {
        public int ProductionStageId { get; init; }
        public int WorkerId { get; init; }
        public string WorkerName { get; init; } = string.Empty;
    }

    /// <summary>
    /// يوم فيه شغل على المنتج، معروض في قايمة "كرّر يوم فات" عشان
    /// المستخدم يختار منه بدل ما يخمّن تاريخ.
    /// </summary>
    public class FlowDayOptionDto
    {
        public DateTime Date { get; init; }

        /// <summary>عدد العمال اللي اشتغلوا في اليوم ده</summary>
        public int WorkerCount { get; init; }

        /// <summary>عدد المراحل اللي اتسجل عليها شغل</summary>
        public int StageCount { get; init; }

        public string DateText => Date.ToString("dddd yyyy/MM/dd");

        public string SummaryText => $"{WorkerCount} عامل على {StageCount} مرحلة";
    }
}
