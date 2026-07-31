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

        /// <summary>
        /// إيه اللي حصل لكل دفعة اتحركت: خلصت الخط، وقفت عند مرحلة، أو
        /// اتقسمت. الرسالة بعد الحفظ بتتبني منها عشان المستخدم يعرف إيه
        /// اللي هيترحّل لبكرة قبل ما يقفل اليوم.
        /// </summary>
        public List<BatchMovementDto> BatchMovements { get; init; } = new();

        /// <summary>عدد الدفعات اللي خلصت الخط كله في الحفظة دي</summary>
        public int CompletedBatches => BatchMovements.Count(m => m.IsCompleted);

        /// <summary>قطع لسه واقفة من الحفظة دي (هتترحّل لبكرة)</summary>
        public int StillOpenPieces => BatchMovements
            .Where(m => !m.IsCompleted)
            .Sum(m => m.Pieces) + BatchMovements.Sum(m => m.LeftBehindPieces);
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
}
