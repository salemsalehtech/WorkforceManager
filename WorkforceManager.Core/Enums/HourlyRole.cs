namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// دور العامل اللي بيتحاسب بالساعة (مش بالقطعة). العامل اللي ليه
    /// دور من دول بيتحسب أجره بعدد الساعات مش بالإنتاج — زي عمال الرص
    /// والجودة والتدريب اللي بيشتغلوا شيفت (8 ساعات = يومية) مش على
    /// مراحل إنتاج محددة.
    /// </summary>
    public enum HourlyRole
    {
        /// <summary>عامل تحت التدريب</summary>
        Training = 1,

        /// <summary>عامل رص</summary>
        Racking = 2,

        /// <summary>عامل جودة</summary>
        Quality = 3,

        /// <summary>دور آخر بالساعة (يُوضّح في ملاحظة العامل)</summary>
        Other = 4,

        /// <summary>
        /// مدير قسم — حساب إداري منفصل تمامًا عن "العمال والمهارات"
        /// (شاشة وقايمة وتقرير لوحدهم). حاضر تلقائيًا كل يوم من غير أي فعل.
        /// </summary>
        DepartmentManager = 5,

        /// <summary>رئيس قسم — نفس معاملة <see cref="DepartmentManager"/> بالضبط</summary>
        DepartmentHead = 6
    }

    /// <summary>الاسم العربي المعروض لكل دور بالساعة</summary>
    public static class HourlyRoleExtensions
    {
        public static string ToArabicName(this HourlyRole role) => role switch
        {
            HourlyRole.Training => "عامل تحت التدريب",
            HourlyRole.Racking => "عامل رص",
            HourlyRole.Quality => "عامل جودة",
            HourlyRole.Other => "دور آخر (بالساعة)",
            HourlyRole.DepartmentManager => "مدير قسم",
            HourlyRole.DepartmentHead => "رئيس قسم",
            _ => "بالساعة"
        };

        /// <summary>حساب إداري (مدير/رئيس قسم) — منفصل تمامًا عن تصنيفات العمال بالساعة التشغيلية</summary>
        public static bool IsDepartmentAccount(this HourlyRole role) =>
            role is HourlyRole.DepartmentManager or HourlyRole.DepartmentHead;
    }
}
