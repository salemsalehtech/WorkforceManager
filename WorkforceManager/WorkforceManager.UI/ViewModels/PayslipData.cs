using WorkforceManager.Business.DTOs;

namespace WorkforceManager.UI.ViewModels
{
    /// <summary>
    /// بيانات قسيمة أجر عامل جاهزة للعرض والطباعة — كل الأرقام متنسّقة
    /// كنصوص، وأعلام الظهور بتتحكم في السطور اللي تظهر (مثلاً السلف/الحوافز
    /// بتظهر بس لو ليها قيمة). بتتبني من تقرير العامل نفسه (مصدر حقيقة واحد).
    /// </summary>
    public class PayslipData
    {
        public string WorkerName { get; private init; } = "";
        public string WorkerSubtitle { get; private init; } = "";
        public string PeriodText { get; private init; } = "";

        public bool ShowPieces { get; private init; }
        public string TotalPiecesText { get; private init; } = "";

        public string ProducedWorkdaysText { get; private init; } = "";

        public bool ShowAbsence { get; private init; }
        public string AbsenceDeductionText { get; private init; } = "";

        public bool ShowPenalty { get; private init; }
        public string PenaltyDeductionText { get; private init; } = "";

        public string NetWorkdaysText { get; private init; } = "";
        public string DailyWageText { get; private init; } = "";
        public string WorkdaysWageText { get; private init; } = "";

        public bool ShowBonus { get; private init; }
        public string BonusText { get; private init; } = "";

        public bool ShowAdvance { get; private init; }
        public string AdvanceText { get; private init; } = "";

        public string NetWageText { get; private init; } = "";
        public string PrintedAtText { get; private init; } = "";

        public static PayslipData From(WorkerProductionReportDto r)
        {
            var days = (r.To.Date - r.From.Date).Days + 1;
            return new PayslipData
            {
                WorkerName = r.WorkerName,
                WorkerSubtitle = r.TypeText,
                PeriodText = $"عن الفترة من {r.From:yyyy/MM/dd} إلى {r.To:yyyy/MM/dd}  ({days} يوم)",

                ShowPieces = !r.IsHourly && r.TotalPieces > 0,
                TotalPiecesText = $"{r.TotalPieces:N0} قطعة",

                ProducedWorkdaysText = $"{r.ProducedWorkdays:0.##} يومية",

                ShowAbsence = r.AbsenceDeduction > 0,
                AbsenceDeductionText = $"− {r.AbsenceDeduction:0.##} يومية",

                ShowPenalty = r.PenaltyDeduction > 0,
                PenaltyDeductionText = $"− {r.PenaltyDeduction:0.##} يومية",

                NetWorkdaysText = $"{r.NetWorkdays:0.##} يومية",
                DailyWageText = r.DailyWageEgp > 0 ? $"{r.DailyWageEgp:N0} ج" : "غير محدد",
                WorkdaysWageText = $"{r.WorkdaysWageEgp:N0} ج",

                ShowBonus = r.BonusEgp > 0,
                BonusText = $"+ {r.BonusEgp:N0} ج",

                ShowAdvance = r.AdvanceEgp > 0,
                AdvanceText = $"− {r.AdvanceEgp:N0} ج",

                NetWageText = $"{r.NetWageEgp:N0} ج",
                PrintedAtText = $"طُبعت في {DateTime.Now:yyyy/MM/dd HH:mm}"
            };
        }
    }
}
