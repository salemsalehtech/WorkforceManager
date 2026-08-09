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
}
