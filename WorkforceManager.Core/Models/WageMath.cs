namespace WorkforceManager.Core.Models
{
    /// <summary>
    /// معادلة الأجر من صافي اليوميات — القاعدة الحسابية الوحيدة، زي
    /// WorkdayMath بالظبط. كانت مكررة يدويًا بنفس التلات سطور في 3 DTOs
    /// (WorkerPayrollDto، WorkerProductionReportDto، WorkerWeeklySummaryDto)،
    /// وأي تغيير في القاعدة (خصم جديد، تعامل مختلف مع السلف/الحوافز) كان
    /// محتاج يتعدّل في كذا مكان.
    /// </summary>
    public static class WageMath
    {
        /// <summary>صافي يوميات الفترة = المنتج − خصم الغياب − خصم الجزاءات</summary>
        public static decimal NetWorkdays(decimal producedWorkdays, decimal absenceDeduction, decimal penaltyDeduction) =>
            producedWorkdays - absenceDeduction - penaltyDeduction;

        /// <summary>أجر اليوميات بالجنيه = الصافي × سعر اليومية (قبل السلف والحوافز)</summary>
        public static decimal WorkdaysWageEgp(decimal netWorkdays, decimal dailyWageEgp) =>
            netWorkdays * dailyWageEgp;

        /// <summary>الأجر النهائي بالجنيه = أجر اليوميات + الحوافز − السلف</summary>
        public static decimal NetWageEgp(decimal workdaysWageEgp, decimal bonusEgp, decimal advanceEgp) =>
            workdaysWageEgp + bonusEgp - advanceEgp;
    }
}
