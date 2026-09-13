using WorkforceManager.Business.DTOs;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// بيشيل من نطاقات الرحلة أي مرحلة مفيش عليها عمال، وبيقول أنهي
    /// مراحل اتشالت.
    ///
    /// ليه ده موجود أصلاً: <see cref="ProductionFlowService.RecordFlowAsync"/>
    /// بترفض أي مرحلة عليها إنتاج ومفيهاش عمال، والرفض "يا كله يا مفيش".
    /// فمستخدم جهّز نطاق من مرحلة لآخر الخط وكمّل نصّه النهارده كان
    /// بيلاقي الحفظ اترفض بالكامل ومفيش ولا سجل اتكتب — وبعدين يشوف
    /// الشغل الواقف زي ما هو ويفتكره عطل.
    ///
    /// النطاق بيتقطّع لمقاطع **متصلة** من المراحل اللي عليها عمال بس، وكل
    /// مقطع بياخد نفس عدد قطع النطاق الأصلي — وده مش تقريب: كل مرحلة في
    /// النطاق أصلاً بتاخد نفس الرقم (شوف <see cref="FlowRangeDto"/>)، فتقطيع
    /// النطاق مابيغيّرش رقم ولا مرحلة واحدة.
    ///
    /// المراحل اللي اتشالت مايتسجّلش عليها حاجة خالص، فبتفضل شغل واقف زي
    /// ما هي — وده بالظبط الصح: اللي اتشتغل يتحفظ، واللي مااتشتغلش يفضل
    /// مستني في أي يوم تاني.
    ///
    /// دالة خالصة (بلا قاعدة بيانات وبلا واجهة) عشان الاختبارات تغطيها
    /// من غير ViewModel — نفس نمط <see cref="WorkerAssignmentGuard.Evaluate"/>
    /// و WorkerFilterRules.
    /// </summary>
    public static class FlowRangeTrimmer
    {
        /// <param name="lineStageIds">
        /// مراحل خط الإنتاج بترتيبها، من غير مرحلة الرص — نفس اللي
        /// ProductionLine.Active بترجّعه، عشان المشي هنا يبقى على نفس
        /// الخط اللي الخدمة بتمشي عليه.
        /// </param>
        /// <param name="staffedStageIds">المراحل اللي عليها عمال فعلاً دلوقتي</param>
        public static FlowTrimResult Trim(
            IReadOnlyList<FlowRangeDto> ranges,
            IReadOnlyList<int> lineStageIds,
            ISet<int> staffedStageIds)
        {
            var indexByStageId = lineStageIds
                .Select((stageId, index) => (stageId, index))
                .ToDictionary(x => x.stageId, x => x.index);

            var trimmed = new List<FlowRangeDto>();
            var droppedStageIds = new List<int>();

            foreach (var range in ranges)
            {
                // طرف مش من الخط (مرحلة رص مثلاً): بيعدّي زي ما هو —
                // الخدمة هي اللي ترفضه برسالتها، مش إحنا
                if (!indexByStageId.TryGetValue(range.FromStageId, out var fromIndex) ||
                    !indexByStageId.TryGetValue(range.ToStageId, out var toIndex))
                {
                    trimmed.Add(range);
                    continue;
                }

                // نطاق معكوس: بيعدّي زي ما هو عشان الخدمة تشرح الغلط
                if (fromIndex > toIndex)
                {
                    trimmed.Add(range);
                    continue;
                }

                // مقاطع متصلة من المراحل اللي عليها عمال. اللفة بتوصل
                // toIndex + 1 عن قصد عشان المقطع اللي واصل لآخر النطاق
                // يتقفل هو كمان
                int? runStart = null;
                for (var i = fromIndex; i <= toIndex + 1; i++)
                {
                    if (i <= toIndex && staffedStageIds.Contains(lineStageIds[i]))
                    {
                        runStart ??= i;
                        continue;
                    }

                    if (i <= toIndex) droppedStageIds.Add(lineStageIds[i]);

                    if (runStart is { } start)
                    {
                        trimmed.Add(new FlowRangeDto
                        {
                            FromStageId = lineStageIds[start],
                            ToStageId = lineStageIds[i - 1],
                            PieceCount = range.PieceCount
                        });
                        runStart = null;
                    }
                }
            }

            return new FlowTrimResult { Ranges = trimmed, DroppedStageIds = droppedStageIds };
        }
    }

    /// <summary>نتيجة تقطيع النطاقات: اللي هيتبعت للحفظ، واللي اتشال</summary>
    public class FlowTrimResult
    {
        public IReadOnlyList<FlowRangeDto> Ranges { get; init; } = Array.Empty<FlowRangeDto>();

        /// <summary>المراحل اللي اتشالت لأنها مفيهاش عمال — بترتيب الخط</summary>
        public IReadOnlyList<int> DroppedStageIds { get; init; } = Array.Empty<int>();

        public bool HasDropped => DroppedStageIds.Count > 0;
    }
}
