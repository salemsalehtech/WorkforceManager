namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// نقطة واحدة في الرسم البياني للمنتجات: منتج معين في فترة معينة
    /// (يوم أو أسبوع أو شهر — حسب التقسيم المختار).
    ///
    /// كان اسمه ProductWeeklyPointDto وبيقسّم بالأسبوع بس، فالمستخدم
    /// اللي عايز يشوف يوم بيوم أو شهر بشهر مكانش قدامه غير إنه يعد
    /// بالعين.
    /// </summary>
    public class ProductOutputPointDto
    {
        /// <summary>أول يوم في الفترة</summary>
        public DateTime BucketStart { get; init; }

        /// <summary>آخر يوم في الفترة</summary>
        public DateTime BucketEnd { get; init; }

        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        /// <summary>
        /// القطع اللي خرجت من الخط: المسجلة على آخر مرحلة، ناقص هالك
        /// آخر مرحلة. (مجموع قطع كل المراحل بيضلل لأنه بيعد نفس القطعة
        /// أكتر من مرة وهي بتعدي على المراحل.)
        /// </summary>
        public int CompletedPieces { get; init; }

        /// <summary>
        /// الهالك المسجّل على المنتج في الفترة — **على كل المراحل**، مش
        /// آخر مرحلة بس. القطعة اللي اترمت في أول الخط هالك برضه.
        /// </summary>
        public int ScrapPieces { get; init; }
    }
}
