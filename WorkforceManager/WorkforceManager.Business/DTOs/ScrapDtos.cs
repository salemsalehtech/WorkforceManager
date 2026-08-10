namespace WorkforceManager.Business.DTOs
{
    /// <summary>سجل هالك واحد جاهز للعرض — من غير كيانات قاعدة البيانات</summary>
    public class ScrapRecordDto
    {
        public int Id { get; init; }
        public DateTime Date { get; init; }

        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";

        public int ProductionStageId { get; init; }
        public string StageName { get; init; } = "";

        public int PieceCount { get; init; }

        /// <summary>null لو السبب اتشال من الإعدادات بعد التسجيل</summary>
        public string? ReasonName { get; init; }

        public string? Note { get; init; }

        public string StageDisplay => $"{ProductName} — {StageName}";
        public string ReasonDisplay => ReasonName ?? "بدون سبب";
    }
}
