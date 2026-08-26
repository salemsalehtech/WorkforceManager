namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// حالة الرصيد الأولي — محسوبة دايمًا من مجموع استخداماته
    /// (InitialBalanceUsage) مقابل كميته الكلية، مش مخزّنة كعمود
    /// مستقل، لنفس سبب DailyProduction.WorkdaysCompleted: تفادي عدم
    /// التطابق لو اتسجل استخدام من غير ما حد يحدّث عمود الحالة.
    /// </summary>
    public enum InitialBalanceStatus
    {
        /// <summary>لسه محدش استخدم أي قطعة منه</summary>
        Available = 1,

        /// <summary>اتاخد جزء منه وفضل جزء متاح</summary>
        PartiallyUsed = 2,

        /// <summary>كل كميته اتاخدت</summary>
        Completed = 3
    }
}
