using System.Collections.Generic;
using System.Linq;
using WorkforceManager.Core.Models;

namespace WorkforceManager.Core.Helpers
{
    /// <summary>
    /// حساب "كام قطعة اتاخدت فعلًا من نطاق رصيد أولي" — مشترك بين
    /// InitialBalanceService (Business) و InitialBalanceRepository (Data)،
    /// عشان الاتنين يتفقوا على نفس التعريف من غير ما Data يعتمد على Business
    /// (اتجاه الاعتماد Core &lt;- Data &lt;- Business ثابت).
    /// </summary>
    public static class InitialBalanceRangeMath
    {
        /// <summary>
        /// كام قطعة اتاخدت فعلًا من نطاق معيّن — **مش** كل استخدام مرتبط
        /// بيه بيتحسب: سحب هالك (<see cref="InitialBalanceUsage.ProductionScrapId"/>
        /// موجود) بيتحسب دايمًا لأنه استهلاك نهائي، لكن سحب إكمال إنتاج
        /// (<see cref="InitialBalanceUsage.DailyProductionId"/> موجود)
        /// بيتحسب بس لو وصل **مرحلة خروج النطاق** — غيره صفوف وسيطة.
        /// </summary>
        public static int UsedQuantity(InitialBalanceRange range, IEnumerable<InitialBalanceUsage> usages) =>
            usages
                .Where(u => u.InitialBalanceRangeId == range.Id)
                .Where(u => u.ProductionScrapId is not null || u.ProductionStageId == range.ToStageId)
                .Sum(u => u.Quantity);
    }
}
