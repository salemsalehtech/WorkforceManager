namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// تفاصيل إنتاج عامل على منتج/مرحلة واحدة — بند في تفصيل شغله.
    ///
    /// كان عايش جوه WorkerDailySummaryDto، واتنقل هنا لما التقييم
    /// اليومي اتشال: الملخص الأسبوعي لسه بيستخدمه.
    /// </summary>
    public class StageBreakdownDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string StageName { get; set; } = string.Empty;
        public int PieceCount { get; set; }
        public int PiecesPerWorkday { get; set; }

        /// <summary>عدد اليوميات المنجزة في هذه المرحلة تحديدًا</summary>
        public decimal Workdays => PiecesPerWorkday == 0 ? 0 : Math.Round((decimal)PieceCount / PiecesPerWorkday, 2);
    }
}
