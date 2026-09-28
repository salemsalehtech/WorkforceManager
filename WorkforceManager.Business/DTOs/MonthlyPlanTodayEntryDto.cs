namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// منتج واحد في تبويب "الإنتاج اليومي" — دايمًا عن النهارده الحقيقي،
    /// مستقل عن أي تنقّل شهر/سنة في تبويب "التخطيط". IsFilled بيعتمد على
    /// وجود صف MonthlyPlanDailyEntry، مش على قيمته (صفر متسجل = مُدخل).
    /// RealProductionToday/YesterdayQuantity مرجع بس — لزرار "استخدم نفس
    /// الرقم الحقيقي"/"انسخ إنتاج أمبارح"، مش جزء من المحقق نفسه.
    /// </summary>
    public record MonthlyPlanTodayEntryDto(
        int ProductId,
        string ProductName,
        int? FamilyId,
        string? FamilyName,
        int ManualQuantity,
        bool IsFilled,
        int RealProductionToday,
        int YesterdayQuantity);
}
