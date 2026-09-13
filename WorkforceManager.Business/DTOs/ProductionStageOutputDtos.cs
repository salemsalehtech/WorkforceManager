namespace WorkforceManager.Business.DTOs
{
    /// <summary>صف إنتاج فعلي واحد — من الجدول الجديد أو من الحساب القديم كخط رجوع</summary>
    public class ProductionOutputRecordDto
    {
        public DateTime Date { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public int ProductionStageId { get; init; }
        public string StageName { get; init; } = "";
        public int PieceCount { get; init; }
    }
}
