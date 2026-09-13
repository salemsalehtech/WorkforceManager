namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// مقدار الخصم المقرر في الجزاء، بوحدة "اليوميات". القيم ثابتة ومتفق
    /// عليها مع مدير القسم: نص يوم / يوم / 3 أيام / أسبوع (= 6 يوميات شغل
    /// فعلية، لأن الجمعة إجازة فأسبوع الشغل 6 أيام مش 7).
    /// </summary>
    public enum PenaltyDeduction
    {
        /// <summary>خصم نص يومية</summary>
        HalfDay = 1,

        /// <summary>خصم يومية كاملة</summary>
        OneDay = 2,

        /// <summary>خصم 3 يوميات</summary>
        ThreeDays = 3,

        /// <summary>خصم أسبوع شغل كامل (6 يوميات)</summary>
        OneWeek = 4
    }

    /// <summary>
    /// تحويل قيمة الجزاء من Enum لعدد يوميات فعلي يُخصم من رصيد العامل.
    /// موجودة هنا (مش في طبقة الـ Business) لأن القيمة جزء من تعريف الجزاء
    /// نفسه مش قاعدة حسابية قابلة للتغيير حسب السياق.
    /// </summary>
    public static class PenaltyDeductionExtensions
    {
        public static decimal ToWorkdays(this PenaltyDeduction deduction) => deduction switch
        {
            PenaltyDeduction.HalfDay => 0.5m,
            PenaltyDeduction.OneDay => 1m,
            PenaltyDeduction.ThreeDays => 3m,
            PenaltyDeduction.OneWeek => 6m, // أسبوع شغل = 6 أيام (الجمعة إجازة)
            _ => 0m
        };

        /// <summary>الاسم المعروض بالعربي في الشاشات والتقارير</summary>
        public static string ToArabicName(this PenaltyDeduction deduction) => deduction switch
        {
            PenaltyDeduction.HalfDay => "نص يوم",
            PenaltyDeduction.OneDay => "يوم",
            PenaltyDeduction.ThreeDays => "3 أيام",
            PenaltyDeduction.OneWeek => "أسبوع",
            _ => "غير محدد"
        };
    }
}
