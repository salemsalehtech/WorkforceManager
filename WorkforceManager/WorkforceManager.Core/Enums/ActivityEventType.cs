namespace WorkforceManager.Core.Enums
{
    /// <summary>
    /// نوع الحدث المسجّل في سجل العمليات.
    ///
    /// الأرقام ثابتة عن قصد: بتتخزن في الداتابيز، فإعادة ترتيبها بتغيّر
    /// معنى كل صف قديم. أي نوع جديد ياخد رقم جديد ومياخدش رقم فاضي من
    /// اللي فوق.
    ///
    /// **الأنواع من 12 لفوق اتضافت لما السجل بقى بيسجّل كل عملية ليها
    /// قيمة مش الحذف بس.** الأنواع من 6 لـ 11 كانت موجودة في القايمة
    /// من زمان وليها أسماء وسياسة احتفاظ — بس **محدش كان بيكتبها**،
    /// فالسجل عمليًا كان سجل حذف. دلوقتي كلها ليها مكان بيكتبها.
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
        OperationsPasswordChanged = 11,

        /// <summary>رحلة إنتاج اتسجلت (المصدر الأساسي لكل يوميات العمال)</summary>
        ProductionRecorded = 12,

        /// <summary>حضور يوم اتحفظ</summary>
        AttendanceSaved = 13,

        /// <summary>يوم إنتاج اتقفل — بعده مفيش تسجيل جديد</summary>
        ProductionDayClosed = 14,

        /// <summary>يوم مقفول اترجع يفتح</summary>
        ProductionDayReopened = 15,

        /// <summary>هالك اتسجل — قطع خرجت من الخط ومش هتكمّل</summary>
        ScrapRecorded = 16,

        /// <summary>عامل جديد اتضاف</summary>
        WorkerCreated = 17,

        /// <summary>منتج جديد اتضاف</summary>
        ProductCreated = 18,

        /// <summary>مرحلة جديدة اتضافت لمنتج</summary>
        StageCreated = 19,

        /// <summary>سلفة أو حافز اتشال</summary>
        WageAdjustmentDeleted = 20,

        /// <summary>
        /// سجل إنتاج اتنقل من عامل لعامل تاني (اتسجّل على عامل غلط
        /// بالغلط) — اليومية بتتحول من القديم للجديد، مش تعديل قطع بس
        /// </summary>
        ProductionWorkerReassigned = 21,

        /// <summary>
        /// تراجع عن آخر عملية على سجل إنتاج (تصحيح قطع، نقل عامل، أو
        /// حذف) — زرار "تراجع" في تبويب سجلات اليوم أو Ctrl+Z
        /// </summary>
        ProductionRecordUndone = 22

        // مفيش نوع لاسترجاع النسخة الاحتياطية عن قصد: الاسترجاع بيستبدل
        // ملف قاعدة البيانات كله وبيعيد تشغيل البرنامج، فالحدث اللي
        // هيتكتب قبله بيتمسح مع الملف القديم واللي بعده مفيش. النوع
        // اللي محدش يقدر يكتبه أسوأ من عدم وجوده.
    }
}
