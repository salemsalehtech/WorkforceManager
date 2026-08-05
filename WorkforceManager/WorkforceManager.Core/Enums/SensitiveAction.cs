namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// العمليات اللي لازم تعدّي على بوابة كلمة سر العمليات قبل ما تتنفّذ.
    ///
    /// القايمة دي مقصود إنها **صريحة ومحصورة**: العمليات اللي بتمسح شغل
    /// أو بتلمس فلوس. تسجيل الإنتاج نفسه **مش** هنا عن قصد — هو الشغل
    /// اليومي المتكرر، وكلمة سر على كل حفظة معناها إن المستخدم هيدوّر
    /// على طريقة يلفّ بيها حوالين النظام.
    ///
    /// الحضور دخل القايمة رغم إنه شغل يومي، والفرق إنه بيتحفظ **دفعة
    /// واحدة لكل القسم** (RecordAttendanceBatchAsync): كلمة سر واحدة في
    /// اليوم، مش واحدة لكل عامل. وهو بيولّد جزاءات غياب تلقائية بتنقص
    /// من الأجر، فهو فعليًا عملية بتلمس فلوس.
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
        SaveWageAdjustment = 8,

        /// <summary>حفظ حضور اليوم (دفعة واحدة لكل القسم)</summary>
        SaveAttendance = 9
    }
}
