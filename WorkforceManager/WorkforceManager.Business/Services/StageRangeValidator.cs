using WorkforceManager.Business.DTOs;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// التحقق المشترك من نطاقات مراحل الإنتاج (من مرحلة X لمرحلة Y = N قطعة):
    /// الترتيب لازم يكون من الأسبق للأحدث في خط الإنتاج، ومفيش مرحلة
    /// تقع في أكتر من نطاق واحد (تسجيل مزدوج بيضاعف اليوميات والأجور).
    ///
    /// نفس المنطق كان مكرر بشكلين مختلفين: <see cref="ProductionFlowService.RecordFlowAsync"/>
    /// كانت بتتحقق من التداخل، و<see cref="InitialBalanceService"/> كانت
    /// بتتحقق من الترتيب بس من غير تداخل — رصيدين بنطاقين متداخلين
    /// كانوا يعدّوا من غير اعتراض. دلوقتي القاعدتين بيستخدموا نفس
    /// المنطق بالظبط.
    /// </summary>
    public static class StageRangeValidator
    {
        /// <summary>
        /// يتحقق من كل النطاقات المُقدَّمة مع بعض (ترتيب + تداخل)، ويرجع
        /// عدد القطع لكل مرحلة (فهرسها بترتيب <paramref name="orderedStages"/>).
        /// <paramref name="rangeIndexByStage"/> بيرجع أنهي نطاق (فهرسه في
        /// <paramref name="ranges"/>) حجز كل مرحلة، أو -1 لو المرحلة مش
        /// داخلة في أي نطاق. بيرمي <see cref="InvalidOperationException"/>
        /// عند أول مخالفة.
        /// </summary>
        public static int[] ValidateAndComputePiecesPerStage(
            List<ProductionStage> orderedStages, IReadOnlyList<FlowRangeDto> ranges, out int[] rangeIndexByStage)
        {
            var indexByStageId = orderedStages
                .Select((stage, index) => (stage.Id, index))
                .ToDictionary(x => x.Id, x => x.index);

            var piecesPerStage = new int[orderedStages.Count];

            // أنهي نطاق حجز أنهي مرحلة. الرقم ده بيدخل في رسالة الخطأ:
            // "متسجلة في النطاق رقم 1" أنفع بكتير من "فيه تداخل" لما يكون
            // المستخدم كاتب 4 نطاقات وبيدوّر على الغلط فيهم — وبيترجع
            // للمنادي كمان (rangeIndexByStage) عشان يربط كل صف إنتاج
            // بالنطاق الأصلي اللي جه منه
            var claimedByRange = new int[orderedStages.Count];
            for (var i = 0; i < claimedByRange.Length; i++) claimedByRange[i] = -1;

            for (var rangeNumber = 0; rangeNumber < ranges.Count; rangeNumber++)
            {
                var range = ranges[rangeNumber];

                if (!indexByStageId.TryGetValue(range.FromStageId, out var fromIndex) ||
                    !indexByStageId.TryGetValue(range.ToStageId, out var toIndex))
                    throw new InvalidOperationException(
                        $"النطاق رقم {rangeNumber + 1} بيشاور على مرحلة مش من مراحل المنتج المحدد");

                if (fromIndex > toIndex)
                    throw new InvalidOperationException(
                        $"النطاق رقم {rangeNumber + 1} معكوس: \"{orderedStages[fromIndex].StageName}\" بتيجي بعد " +
                        $"\"{orderedStages[toIndex].StageName}\" في خط الإنتاج — راجع الترتيب");

                if (range.PieceCount <= 0)
                    throw new InvalidOperationException(
                        $"عدد القطع في النطاق رقم {rangeNumber + 1} لازم يكون رقمًا موجبًا");

                for (var i = fromIndex; i <= toIndex; i++)
                {
                    // نفس المرحلة ميصحش تقع في نطاقين — ده تسجيل مزدوج
                    // بيضاعف يوميات العمال وأجورهم
                    if (piecesPerStage[i] != 0)
                        throw new InvalidOperationException(
                            $"المرحلة \"{orderedStages[i].StageName}\" متسجلة خلاص في النطاق رقم " +
                            $"{claimedByRange[i] + 1}، ومش هينفع تتسجل تاني في النطاق رقم {rangeNumber + 1} — " +
                            $"المرحلة الواحدة بتتحسب مرة واحدة في اليوم");

                    piecesPerStage[i] = range.PieceCount;
                    claimedByRange[i] = rangeNumber;
                }
            }

            rangeIndexByStage = claimedByRange;
            return piecesPerStage;
        }
    }
}
