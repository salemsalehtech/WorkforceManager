namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// مصدر الرصيد الأولي: هل المستخدم أضافه يدويًا من شاشة الرصيد،
    /// ولا هو ناتج تلقائي عن قطع ناقصة في رحلة إنتاج يومية.
    /// </summary>
    public enum InitialBalanceSource
    {
        /// <summary>أضافه المستخدم يدويًا من شاشة الرصيد الأولي</summary>
        Manual = 1,

        /// <summary>ناتج عن قطع ناقصة (بدأت ولم تكتمل) في رحلة إنتاج يومية</summary>
        DailyProduction = 2,

        /// <summary>ناتج عن ترحيل تلقائي لشغل واقف كان موجود قبل هذا الفيتشر (مرة واحدة، أول تشغيل)</summary>
        Migrated = 3
    }
}
