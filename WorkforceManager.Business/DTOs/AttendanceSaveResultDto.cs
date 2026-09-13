namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// نتيجة حفظ الحضور — عشان رسالة النجاح تقول للمستخدم بالظبط اللي
    /// حصل، خصوصًا الجزاءات اللي النظام ولّدها أو شالها لوحده. من غير
    /// كده كان هيلاقي جزاءات ظهرت أو اختفت من غير ما يعرف مين عملها.
    /// </summary>
    public class AttendanceSaveResultDto
    {
        /// <summary>عدد العمال اللي اتسجلت/اتحدّثت حالتهم</summary>
        public int SavedCount { get; init; }

        /// <summary>عدد جزاءات الغياب التلقائية اللي اتولّدت في الحفظة دي</summary>
        public int AutoPenaltiesCreated { get; init; }

        /// <summary>عدد جزاءات الغياب التلقائية اللي اتشالت (حالة العامل اتغيّرت)</summary>
        public int AutoPenaltiesRemoved { get; init; }
    }
}
