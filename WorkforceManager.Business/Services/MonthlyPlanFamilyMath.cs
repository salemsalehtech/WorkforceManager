namespace WorkforceManager.Business.Services
{
    /// <summary>
    /// الإنتاج اليومي المطلوب على مستوى عيلة/مجموعة كاملة — صيغة واحدة على
    /// الإجمالي (planSum - achievedSum) ÷ الأيام المتبقية، مش مجموع أرقام
    /// منتجات مقرّبة كل واحدة لوحدها. مشتركة بين شاشة الخطة الشهرية
    /// (MonthlyPlanFamilyGroupRow) وتصديرها (MonthlyPlanExcelService) —
    /// مكان واحد، ما ينفعش الاتنين يختلفوا، شوف CLAUDE.md.
    /// </summary>
    public static class MonthlyPlanFamilyMath
    {
        public static int? RequiredDailyOutput(int planSum, int achievedSum, int remainingWorkdays) =>
            remainingWorkdays > 0
                ? (int)Math.Max(0, Math.Ceiling((planSum - achievedSum) / (decimal)remainingWorkdays))
                : null;
    }
}
