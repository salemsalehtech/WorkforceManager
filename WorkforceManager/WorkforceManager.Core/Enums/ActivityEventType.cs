namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// نوع الحدث المسجّل في سجل العمليات.
    ///
    /// الأرقام ثابتة عن قصد: بتتخزن في الداتابيز، فإعادة ترتيبها بتغيّر
    /// معنى كل صف قديم. أي نوع جديد ياخد رقم جديد ومياخدش رقم فاضي من
    /// اللي فوق.
    /// </summary>
    public enum ActivityEventType
    {
        /// <summary>يوم إنتاج كامل اتشال</summary>
        ProductionDayDeleted = 1,

        /// <summary>سجل إنتاج واحد اتشال</summary>
        ProductionRecordDeleted = 2,

        /// <summary>عامل اتشال</summary>
        WorkerDeleted = 3,

        /// <summary>منتج اتشال</summary>
        ProductDeleted = 4,

        /// <summary>مرحلة إنتاج اتشالت</summary>
        StageDeleted = 5,

        /// <summary>سعر يومية عامل اتغيّر</summary>
        WorkerWageChanged = 6,

        /// <summary>عدد قطع سجل إنتاج اتصحّح</summary>
        ProductionPiecesEdited = 7,

        /// <summary>جزاء اتسجل أو اتعدّل</summary>
        PenaltySaved = 8,

        /// <summary>جزاء اتشال</summary>
        PenaltyDeleted = 9,

        /// <summary>سلفة أو حافز اتسجل</summary>
        WageAdjustmentSaved = 10,

        /// <summary>كلمة سر العمليات اتغيّرت</summary>
        OperationsPasswordChanged = 11
    }
}
