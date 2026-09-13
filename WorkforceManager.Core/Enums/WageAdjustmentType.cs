namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// نوع تعديل الأجر بالجنيه المسجّل على العامل خلال الفترة:
    /// - سلفة (Advance): مبلغ أخذه العامل مقدمًا، يُخصم من صافي أجره.
    /// - حافز (Bonus): مبلغ يُضاف لأجره (مكافأة إنتاج، عيد، تارجت...).
    /// كلاهما بالجنيه (مش يوميات) — عكس الجزاء اللي بيتخصم يوميات.
    /// </summary>
    public enum WageAdjustmentType
    {
        /// <summary>سلفة/مسحوبات — تُخصم من الأجر</summary>
        Advance = 0,

        /// <summary>حافز/مكافأة — تُضاف للأجر</summary>
        Bonus = 1
    }

    /// <summary>أدوات مساعدة لعرض نوع التعديل بالعربي</summary>
    public static class WageAdjustmentTypeExtensions
    {
        public static string ToArabicName(this WageAdjustmentType type) => type switch
        {
            WageAdjustmentType.Advance => "سلفة",
            WageAdjustmentType.Bonus => "حافز",
            _ => type.ToString()
        };
    }
}
