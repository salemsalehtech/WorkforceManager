namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// العمليات اللي لازم تعدّي على بوابة كلمة سر العمليات قبل ما تتنفّذ.
    ///
    /// القايمة دي مقصود إنها **صريحة ومحصورة**: العمليات اللي بتمسح شغل
    /// أو بتلمس فلوس بس. الشغل اليومي المتكرر (تسجيل الحضور، فتح يوم
    /// إنتاج، تسجيل الإنتاج) **مش** هنا عن قصد — كلمة سر كل يوم على كل
    /// عامل معناها إن المستخدم هيدوّر على طريقة يلفّ بيها حوالين النظام.
    ///
    /// إضافة عملية جديدة = بند جديد هنا + نداء واحد للبوابة. مفيش شاشة
    /// بتتحقق من كلمة السر بنفسها.
    /// </summary>
    public enum SensitiveAction
    {
        /// <summary>حذف عامل</summary>
        DeleteWorker = 1,

        /// <summary>حذف منتج</summary>
        DeleteProduct = 2,

        /// <summary>حذف مرحلة إنتاج</summary>
        DeleteStage = 3,

        /// <summary>حذف سجل إنتاج أو يوم إنتاج كامل</summary>
        DeleteProduction = 4,

        /// <summary>تعديل سعر يومية العامل</summary>
        EditWorkerWage = 5,

        /// <summary>تعديل عدد قطع سجل إنتاج محفوظ</summary>
        EditProductionPieces = 6,

        /// <summary>حفظ أو تعديل جزاء</summary>
        SavePenalty = 7,

        /// <summary>تسجيل أو تعديل سلفة/حافز</summary>
        SaveWageAdjustment = 8
    }
}
