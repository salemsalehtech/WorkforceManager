using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;
using WorkforceManager.Core.Enums;
using WorkforceManager.Core.Helpers;
using WorkforceManager.Core.Interfaces;
using WorkforceManager.Data;
using WorkforceManager.UI.Views;

namespace WorkforceManager.UI.ViewModels
{
    // هستوري العامل الأسبوعي: كارت كل أسبوع وتفاصيله جوّه بروفايله.
    // WeekHistoryItem هي الكارت، وجوّاها صفوف المراحل والجزاءات.

    /// <summary>
    /// كارت أسبوع واحد في هستوري العامل. مقفول بيوري الرقمين اللي بيتسألوا
    /// عنهم فعلاً (الصافي والأجر)، وبيتفتح على التفاصيل كصفوف — قبل كده كانت
    /// التفاصيل سطر نص واحد بيلف ("GRS/دبله: 9999 قطعة، GRS/رقبه: ...")
    /// ومستحيل تقرا منه رقم مرحلة معينة.
    /// </summary>
    public partial class WeekHistoryItem : ObservableObject
    {
        /// <summary>مدى الأسبوع بالتاريخ (يوم/شهر — يوم/شهر)</summary>
        public string WeekTitle { get; init; } = "";

        /// <summary>"الأسبوع الحالي" / "الأسبوع اللي فات" — فاضي لباقي الأسابيع</summary>
        public string RelativeLabel { get; init; } = "";
        public bool HasRelativeLabel => RelativeLabel.Length > 0;

        public decimal Produced { get; init; }
        public decimal AbsenceDeduction { get; init; }
        public decimal PenaltyDeduction { get; init; }
        public decimal Net { get; init; }

        /// <summary>أحسن عامل في الأسبوع ده</summary>
        public bool IsBest { get; init; }

        public string WageText { get; init; } = "";
        public bool HasWage => WageText.Length > 0;

        // الأرقام كنص منسّق — بدل ما XAML يعرض 25.0000 من الـ decimal الخام
        public string ProducedText => $"{Produced:0.##}";
        public string NetText => $"{Net:0.##}";
        public string AbsenceText => $"−{AbsenceDeduction:0.##}";
        public string PenaltyText => $"−{PenaltyDeduction:0.##}";

        public bool HasAbsence => AbsenceDeduction > 0;
        public bool HasPenaltyDeduction => PenaltyDeduction > 0;

        public ObservableCollection<WeekStageRow> Breakdown { get; init; } = new();
        public ObservableCollection<WeekPenaltyRow> Penalties { get; init; } = new();

        public bool HasBreakdown => Breakdown.Count > 0;
        public bool HasPenalties => Penalties.Count > 0;

        /// <summary>أسبوع مفيهوش أي شغل — بيتعرض مطفي ومبيتفتحش</summary>
        public bool IsEmptyWeek => Produced == 0 && !HasAbsence && !HasPenaltyDeduction;

        /// <summary>عدد المراحل اللي اشتغل عليها — بيبان على الكارت المقفول</summary>
        public string StagesCountText => Breakdown.Count == 1 ? "مرحلة واحدة" : $"{Breakdown.Count} مراحل";

        [ObservableProperty]
        private bool _isExpanded;
    }

    /// <summary>سطر إنتاج واحد جوّه كارت الأسبوع (منتج / مرحلة / قطع)</summary>
    public class WeekStageRow
    {
        public string ProductName { get; init; } = "";
        public string StageName { get; init; } = "";
        public int PieceCount { get; init; }

        public string PiecesText => $"{PieceCount:N0} قطعة";
    }

    /// <summary>سطر جزاء واحد جوّه كارت الأسبوع</summary>
    public class WeekPenaltyRow
    {
        public string Reason { get; init; } = "";
        public string DeductionName { get; init; } = "";
        public string DateText { get; init; } = "";
    }
}
