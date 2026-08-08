using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Interfaces;

namespace WorkforceManager.UI.ViewModels
{
    // صفوف شاشات التقارير: تقييم اليوم والرسم البياني والكشوف
    // والتقرير العام وتقرير العامل. كل نوع بيقابل جدول أو كارت في ReportsView.

    public class DailyProductRow
    {
        public string ProductName { get; init; } = "";

        /// <summary>القطع اللي خلصت الخط كامل = المنتج التام</summary>
        public int Pieces { get; init; }

        /// <summary>القطع اللي دخلت أول مرحلة</summary>
        public int StartedPieces { get; init; }

        public int WorkerCount { get; init; }

        /// <summary>
        /// الرقم على الكارت. لو مفيش تام بيقول اللي دخل الخط بدل ما
        /// يقول صفر — اليوم مش فاضي، هو شغل لسه ماوصلش لآخر الخط.
        /// </summary>
        public string PiecesText => Pieces > 0
            ? $"{Pieces:N0} تام"
            : StartedPieces > 0
                ? $"{StartedPieces:N0} دخلت الخط"
                : "شغل في نص الخط";

        public string WorkersText => $"{WorkerCount} عامل";
    }

    /// <summary>سطر واحد في جدول تقييم اليوم، بتصنيف ملوّن جاهز للعرض</summary>
    public class DailyReportRow
    {
        public string WorkerName { get; private init; } = "";
        public int TotalPieces { get; private init; }
        public decimal TotalWorkdays { get; private init; }
        public string PercentText { get; private init; } = "";
        public string RatingText { get; private init; } = "";
        public string RatingColor { get; private init; } = "#666666";
        public string AttendanceText { get; private init; } = "";
        public string BreakdownText { get; private init; } = "";
        public string PenaltiesText { get; private init; } = "";

        /// <summary>تحويل نتيجة التقييم من الخدمة لشكل العرض (نص + لون لكل تصنيف)</summary>
        public static DailyReportRow From(WorkerDailySummaryDto dto, string penaltiesText)
        {
            var (ratingText, ratingColor) = dto.Rating switch
            {
                PerformanceRating.TopPerformer => ("⭐ الأفضل النهارده", "#B7791F"),
                PerformanceRating.AboveAverage => ("فوق المتوسط", "#0B6E4F"),
                PerformanceRating.Average => ("متوسط", "#666666"),
                PerformanceRating.BelowAverage => ("تحت المتوسط", "#C62828"),
                PerformanceRating.UnexcusedAbsence => ("غياب بدون إذن", "#B00020"),
                _ => ("غير محدد", "#666666")
            };

            return new DailyReportRow
            {
                WorkerName = dto.WorkerName,
                TotalPieces = dto.TotalPieces,
                TotalWorkdays = dto.TotalWorkdays,
                // النسبة مالهاش معنى لو مفيش إنتاج أصلاً
                PercentText = dto.TotalPieces == 0 ? "—" : $"{dto.PercentVsAverage:+0.#;-0.#;0}%",
                RatingText = ratingText,
                RatingColor = ratingColor,
                AttendanceText = dto.AttendanceStatus switch
                {
                    Core.Enums.AttendanceStatus.Present => "حاضر",
                    Core.Enums.AttendanceStatus.AbsentWithPermission => "غياب بإذن",
                    Core.Enums.AttendanceStatus.AbsentWithoutPermission => "غياب بدون إذن",
                    _ => "—"
                },
                BreakdownText = string.Join("، ",
                    dto.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount}")),
                PenaltiesText = penaltiesText
            };
        }
    }

    /// <summary>مجموعة أعمدة أسبوع واحد في رسم إنتاج المنتجات</summary>
    public class ChartWeekGroup
    {
        public string WeekLabel { get; init; } = "";

        /// <summary>إجمالي الأسبوع — بيتكتب فوق العمود كرقم واحد</summary>
        public string TotalText { get; init; } = "";

        /// <summary>شرايح العمود المكدّس، بترتيب المفتاح</summary>
        public List<ChartBar> Segments { get; init; } = new();

        public bool HasWork { get; init; }

        /// <summary>الأسبوع الجاري — لسه مكملش، فبيتعلّم عشان المقارنة بيه ناقصة</summary>
        public bool IsCurrentWeek { get; init; }

        public string CurrentWeekNote => IsCurrentWeek ? "لسه شغال" : "";
    }

    /// <summary>شريحة في العمود المكدّس: منتج في أسبوع (اللون بيميز المنتج)</summary>
    public class ChartBar
    {
        public string Color { get; init; } = "#2563EB";
        public double Height { get; init; }
        public string Tooltip { get; init; } = "";
    }

    /// <summary>عنصر في مفتاح ألوان الرسم: المنتج ولونه وإجماليه في الفترة</summary>
    public class ChartLegendItem
    {
        public string Color { get; init; } = "#1F3864";
        public string ProductName { get; init; } = "";
        public string TotalText { get; init; } = "";
    }

    /// <summary>سطر واحد في كشف أجور الفترة (شهري) — مرتّب بالأجر</summary>
    public class PayrollRow
    {
        public int Rank { get; private init; }
        public string WorkerName { get; private init; } = "";
        public string TypeText { get; private init; } = "";
        public int DaysWorked { get; private init; }
        public decimal NetWorkdays { get; private init; }
        public string DailyWageText { get; private init; } = "";
        public string BonusText { get; private init; } = "";
        public string AdvanceText { get; private init; } = "";
        public string NetWageText { get; private init; } = "";

        public static PayrollRow From(WorkerPayrollDto dto, int rank) => new()
        {
            Rank = rank,
            WorkerName = dto.WorkerName,
            TypeText = dto.IsHourly ? "بالساعة" : "إنتاج",
            DaysWorked = dto.DaysWorked,
            NetWorkdays = dto.NetWorkdays,
            DailyWageText = dto.DailyWageEgp > 0 ? $"{dto.DailyWageEgp:N0} ج" : "لم يُحدد",
            BonusText = dto.BonusEgp > 0 ? $"{dto.BonusEgp:N0} ج" : "—",
            AdvanceText = dto.AdvanceEgp > 0 ? $"{dto.AdvanceEgp:N0} ج" : "—",
            NetWageText = dto.DailyWageEgp > 0 || dto.BonusEgp > 0 || dto.AdvanceEgp > 0 ? $"{dto.NetWageEgp:N0} ج" : "—"
        };
    }

    /// <summary>سطر واحد في كشف الأسبوع (مرتّب بصافي اليوميات)</summary>
    public class WeeklyReportRow
    {
        public int Rank { get; private init; }
        public string BestMark { get; private init; } = "";
        public string WorkerName { get; private init; } = "";
        public decimal ProducedWorkdays { get; private init; }
        public int TotalPieces { get; private init; }
        public int PresentDays { get; private init; }
        public int AbsentWithPermissionDays { get; private init; }
        public int AbsentWithoutPermissionDays { get; private init; }
        public decimal AbsenceDeduction { get; private init; }
        public decimal PenaltyDeduction { get; private init; }
        public decimal NetWorkdays { get; private init; }

        // ------- الأعمدة المدمجة (الكشف اتنضّف من 12 عمود لـ 9) -------

        /// <summary>
        /// الغياب كرقم واحد: "3 (منهم 1 بإذن)".
        ///
        /// كانوا عمودين (بإذن / بدون إذن). المدير بيسأل "غاب كام؟" الأول،
        /// والتفصيل بيهمه بعد كده — فالرقم الكبير قدام والتفصيل جنبه.
        /// </summary>
        public string AbsenceText
        {
            get
            {
                var total = AbsentWithPermissionDays + AbsentWithoutPermissionDays;
                if (total == 0) return "—";

                return AbsentWithPermissionDays == 0
                    ? total.ToString()
                    : $"{total} (منهم {AbsentWithPermissionDays} بإذن)";
            }
        }

        /// <summary>
        /// إجمالي الخصم من غياب وجزاءات.
        ///
        /// كانوا عمودين، والاتنين أصلاً مطروحين من الصافي اللي جنبهم —
        /// يعني نفس الأرقام معروضة مرتين. المدير محتاج يعرف "اتخصم منه
        /// كام" مش "اتخصم منه كام من نوعين"؛ التفصيل في تقرير العامل.
        /// </summary>
        public decimal TotalDeduction => AbsenceDeduction + PenaltyDeduction;

        public string DeductionText => TotalDeduction == 0 ? "—" : $"{TotalDeduction:0.##}";
        public string NetColor { get; private init; } = "#1F3864";
        /// <summary>أجر الأسبوع بالجنيه للعرض (فاضي لو مفيش سعر يومية)</summary>
        public string WageText { get; private init; } = "";
        public string BreakdownText { get; private init; } = "";
        public string PenaltiesText { get; private init; } = "";

        /// <summary>هل فيه تفاصيل تستحق العرض في لوحة التفاصيل؟</summary>
        public bool HasBreakdown => BreakdownText.Length > 0;
        public bool HasPenalties => PenaltiesText.Length > 0;

        public static WeeklyReportRow From(WorkerWeeklySummaryDto dto, int rank) => new()
        {
            Rank = rank,
            BestMark = dto.IsBestWorkerOfWeek ? "⭐" : "",
            WorkerName = dto.WorkerName,
            ProducedWorkdays = dto.ProducedWorkdays,
            TotalPieces = dto.TotalPieces,
            PresentDays = dto.PresentDays,
            AbsentWithPermissionDays = dto.AbsentWithPermissionDays,
            AbsentWithoutPermissionDays = dto.AbsentWithoutPermissionDays,
            AbsenceDeduction = dto.AbsenceDeduction,
            PenaltyDeduction = dto.PenaltyDeduction,
            NetWorkdays = dto.NetWorkdays,
            NetColor = dto.NetWorkdays < 0 ? "#C62828" : "#1F3864", // الصافي السالب أحمر
            WageText = dto.DailyWageEgp > 0 ? $"{dto.NetWageEgp:N0} ج" : "—",
            BreakdownText = string.Join("، ",
                dto.Breakdown.Select(b => $"{b.ProductName}/{b.StageName}: {b.PieceCount} قطعة ({b.Workdays} يومية)")),
            PenaltiesText = string.Join("، ",
                dto.Penalties.Select(p => $"{p.Date:MM/dd} {p.Reason} ({p.DeductionName})"))
        };
    }

    /// <summary>عنصر في قائمة اختيار العامل لتقرير عامل معيّن</summary>
    public class WorkerPickItem
    {
        public int Id { get; init; }
        public string Display { get; init; } = "";
    }

    /// <summary>سطر إنتاج مرحلة في التقارير (منتج/مرحلة + قطع + يوميات)</summary>
    public class GeneralStageRow
    {
        public string ProductName { get; private init; } = "";
        public string StageDisplay { get; private init; } = "";
        public int Pieces { get; private init; }
        public decimal Workdays { get; private init; }

        public static GeneralStageRow From(ProductStageProductionDto dto) => new()
        {
            ProductName = dto.ProductName,
            // آخر مرحلة = إنتاج مكتمل خرج من الخط — بنعلّمها للمستخدم
            StageDisplay = dto.IsLastStage ? $"{dto.StageName} ✅ (مكتمل)" : dto.StageName,
            Pieces = dto.Pieces,
            Workdays = dto.Workdays
        };
    }

    /// <summary>سطر عامل في التقرير العام (مرتّب باليوميات)</summary>
    public class GeneralWorkerRow
    {
        public int Rank { get; private init; }
        public string WorkerName { get; private init; } = "";
        public string TypeText { get; private init; } = "";
        public int TotalPieces { get; private init; }
        public decimal TotalWorkdays { get; private init; }

        public static GeneralWorkerRow From(WorkerProductionSummaryDto dto, int rank) => new()
        {
            Rank = rank,
            WorkerName = dto.WorkerName,
            TypeText = dto.IsHourly ? "بالساعة" : "إنتاج",
            TotalPieces = dto.TotalPieces,
            TotalWorkdays = dto.TotalWorkdays
        };
    }

    /// <summary>سطر يوم في تقرير عامل معيّن</summary>
    public class WorkerDayRow
    {
        public string DateText { get; private init; } = "";
        public int Pieces { get; private init; }
        public decimal Workdays { get; private init; }
        public string Detail { get; private init; } = "";

        public static WorkerDayRow From(WorkerDayProductionDto dto) => new()
        {
            DateText = dto.Date.ToString("yyyy/MM/dd (dddd)"),
            Pieces = dto.Pieces,
            Workdays = dto.Workdays,
            Detail = dto.Detail
        };
    }

    /// <summary>سطر جزاء في تقرير عامل معيّن</summary>
    public class WorkerPenaltyRow
    {
        public string DateText { get; private init; } = "";
        public string Reason { get; private init; } = "";
        public string DeductionName { get; private init; } = "";

        public static WorkerPenaltyRow From(PenaltySummaryDto dto) => new()
        {
            DateText = dto.Date.ToString("yyyy/MM/dd"),
            Reason = dto.Reason,
            DeductionName = dto.Deduction.ToArabicName()
        };
    }
}
