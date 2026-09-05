using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Interfaces
{
    /// <summary>
    /// كتابة رصيد أولي/نطاقه وقت التسجيل (queueing على نفس الـ DbContext،
    /// بدون SaveChanges مستقل — زي IDailyProductionRepository.AddAsync
    /// بالظبط). موجودة عشان ProductionFlowService يقدر ينشئ رصيد تلقائي
    /// من غير ما يعتمد على InitialBalanceService نفسها (اللي هي بتعتمد
    /// على ProductionFlowService — اعتماد دائري لو اتعكس).
    /// </summary>
    public interface IInitialBalanceRepository
    {
        Task AddAsync(InitialBalance balance);

        Task AddRangeAsync(InitialBalanceRange range);

        /// <summary>
        /// كل نطاقات الرصيد الأولي المفتوحة (غير محذوفة) لمنتج معيّن، بالمتبقي
        /// الفعلي لكل واحد منها (PieceCount ناقص المُستخدم — شوف
        /// InitialBalanceRangeMath.UsedQuantity). لمزامنة فجوات خط الإنتاج
        /// بعد كل حفظة (ProductionFlowService) من غير ما تتكرر فجوة اتغطّت خلاص.
        /// </summary>
        Task<IReadOnlyList<(int FromStageId, int ToStageId, int Remaining)>> GetOpenRangeRemainingsAsync(int productId);

        /// <summary>
        /// الأرصدة **التلقائية** (Source == DailyProduction) المفتوحة لمنتج
        /// معيّن، بنطاقاتها واستخداماتها محمّلة — لإعادة فحص الفجوات بعد أي
        /// عملية بترجّع إنتاج المنتج لحالة قبل الحفظ (حذف سجل/يوم كامل)،
        /// شوف ProductionFlowService.ReconcileAutoBalancesAsync.
        /// </summary>
        Task<IReadOnlyList<InitialBalance>> GetOpenAutoBalancesAsync(int productId);

        /// <summary>يشيل رصيد تلقائي بقت فجوته اتقفلت خالص — بدون SaveChanges مستقل، زي AddAsync بالظبط</summary>
        void Remove(InitialBalance balance);
    }
}
