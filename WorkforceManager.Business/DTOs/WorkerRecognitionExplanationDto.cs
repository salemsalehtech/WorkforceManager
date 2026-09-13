namespace WorkforceManager.Business.DTOs
{
    /// <summary>
    /// شرح كامل لسبب فوز عامل بترتيب معين في أسبوع معين — أساس نافذة
    /// "ليه فاز؟" في شاشة العمال. كل الأرقام هنا للعرض فقط، ومالها أي
    /// أثر على الأجر أو صافي اليوميات (WorkerWeeklySummaryDto.NetWorkdays/NetWageEgp).
    /// </summary>
    public class WorkerRecognitionExplanationDto
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;

        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }

        /// <summary>ترتيبه بين المؤهلين هذا الأسبوع (1 = الأول)</summary>
        public int Rank { get; set; }

        /// <summary>عدد العمال المؤهلين للمقارنة هذا الأسبوع (بعد استبعاد عمال الساعة ومن لم ينتج)</summary>
        public int EligibleWorkerCount { get; set; }

        public int TotalPieces { get; set; }
        public List<StageBreakdownDto> Breakdown { get; set; } = new();

        public int DistinctStageCount { get; set; }
        public decimal DiversityFactor { get; set; }
        public decimal AdjustedWorkdays { get; set; }

        public int PresentDays { get; set; }
        public int AbsentWithPermissionDays { get; set; }
        public int AbsentWithoutPermissionDays { get; set; }
        public decimal AbsenceDeduction { get; set; }

        public List<PenaltySummaryDto> Penalties { get; set; } = new();
        public decimal PenaltyDeduction { get; set; }

        /// <summary>درجة الترتيب النهائية — نفس نتيجة WorkerRecognitionRules.RecognitionScore</summary>
        public decimal FinalScore { get; set; }
    }
}
