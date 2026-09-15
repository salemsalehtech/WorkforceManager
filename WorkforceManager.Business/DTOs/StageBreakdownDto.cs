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
        /// <summary>
        /// المرحلة الحقيقية (شوف WorkerRecognitionService.RankForRecognition
        /// اللي بيقرا معامل صعوبتها الحيّ). اسم المرحلة لوحده مش كفاية —
        /// بيتكرر عبر منتجات مختلفة بمعامل صعوبة مختلف لكل واحد.
        /// </summary>
        public int ProductionStageId { get; set; }

        public string ProductName { get; set; } = string.Empty;
        public string StageName { get; set; } = string.Empty;
        public int PieceCount { get; set; }
        public int PiecesPerWorkday { get; set; }

        /// <summary>
        /// عدد اليوميات المنجزة في هذه المرحلة تحديدًا — لازم تتحط من مجموع
        /// WorkdayMath.FromPieces (أو DailyProduction.WorkdaysCompleted) لكل
        /// سجل يوم على حدة، **مش** Math.Round(PieceCount الأسبوعي المجمّع /
        /// PiecesPerWorkday) — ده بالظبط الباج اللي WorkdayMath.cs موثّق إنه
        /// حصل قبل كده (1.00 مقابل 0.99 لنفس اليوم في شاشتين مختلفين).
        /// </summary>
        public decimal Workdays { get; set; }
    }
}
