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
    }
}
