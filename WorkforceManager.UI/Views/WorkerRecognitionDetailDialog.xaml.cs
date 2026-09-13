using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WorkforceManager.Business.DTOs;
using WorkforceManager.Business.Services;

namespace WorkforceManager.UI.Views
{
    /// <summary>
    /// "ليه فاز؟" — شرح مفصّل لترتيب عامل معيّن في أسبوع معيّن، من نتيجة
    /// <see cref="WorkerRecognitionService.GetWeeklyExplanationAsync"/>.
    /// كل الأرقام هنا للعرض فقط ومالها أي أثر على الأجر — التوضيح ده
    /// مكتوب صريح تحت في الشاشة نفسها.
    /// </summary>
    public partial class WorkerRecognitionDetailDialog : Window
    {
        private readonly Action<int> _onOpenProfile;
        private readonly int _workerId;

        private WorkerRecognitionDetailDialog(
            WorkerRecognitionExplanationDto e,
            IReadOnlyDictionary<int, decimal> difficultyByStageId,
            Action<int> onOpenProfile)
        {
            InitializeComponent();

            _onOpenProfile = onOpenProfile;
            _workerId = e.WorkerId;

            TitleText.Text = $"ليه {e.WorkerName} في المركز {Ordinal(e.Rank)}؟";
            SubtitleText.Text = $"الأسبوع من {e.WeekStart:d MMMM} إلى {e.WeekEnd:d MMMM}";

            ExplanationText.Text = BuildExplanationParagraph(e);

            StageList.ItemsSource = e.Breakdown
                .Select(b => new StageExplanationRow
                {
                    ProductName = b.ProductName,
                    StageName = b.StageName,
                    PieceCount = b.PieceCount,
                    PiecesPerWorkday = b.PiecesPerWorkday,
                    Workdays = b.Workdays,
                    DifficultyMultiplier = difficultyByStageId.GetValueOrDefault(b.ProductionStageId, 1.0m)
                })
                .ToList();

            AttendanceText.Text = $"حضور {e.PresentDays} يوم، غياب بإذن {e.AbsentWithPermissionDays} يوم، " +
                                   $"غياب بدون إذن {e.AbsentWithoutPermissionDays} يوم" +
                                   (e.AbsenceDeduction > 0 ? $" (خصم {e.AbsenceDeduction:0.##} يومية)" : "");

            PenaltiesPanel.Visibility = e.Penalties.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PenaltiesList.ItemsSource = e.Penalties;

            ScoreText.Text = $"{e.FinalScore:0.00}";
            RankText.Text = $"المركز {e.Rank} من {e.EligibleWorkerCount} عامل مؤهل للمقارنة هذا الأسبوع";
        }

        /// <summary>يجهّز الشرح ويعرضه. بيرجع بدون عرض حاجة لو العامل مش من ضمن المؤهلين أصلًا (حالة نادرة/تسابق بيانات)</summary>
        public static async Task ShowAsync(
            Window? owner, IServiceScopeFactory scopeFactory,
            int workerId, DateTime weekAnchor, Action<int> onOpenProfile)
        {
            WorkerRecognitionExplanationDto? explanation;
            Dictionary<int, decimal> difficultyByStageId;

            using (var scope = scopeFactory.CreateScope())
            {
                explanation = await scope.ServiceProvider.GetRequiredService<WorkerRecognitionService>()
                    .GetWeeklyExplanationAsync(workerId, weekAnchor);
                difficultyByStageId = await scope.ServiceProvider.GetRequiredService<WeeklySummaryService>()
                    .LoadDifficultyByStageIdAsync();
            }

            if (explanation is null) return;

            var dialog = new WorkerRecognitionDetailDialog(explanation, difficultyByStageId, onOpenProfile);
            if (owner is not null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        private static string Ordinal(int rank) => rank switch
        {
            1 => "الأول",
            2 => "الثاني",
            3 => "الثالث",
            _ => $"رقم {rank}"
        };

        private static string BuildExplanationParagraph(WorkerRecognitionExplanationDto e)
        {
            var stageWord = e.DistinctStageCount switch
            {
                0 => "أي مرحلة",
                1 => "مرحلة واحدة",
                _ => $"{e.DistinctStageCount} مراحل مختلفة"
            };

            var absenceText = e.AbsentWithoutPermissionDays > 0
                ? $"وغياب {e.AbsentWithoutPermissionDays} يوم بدون إذن"
                : "ومن غير أي غياب بدون إذن";

            var penaltyText = e.PenaltyDeduction > 0
                ? $"، وجزاء بقيمة {e.PenaltyDeduction:0.##} يومية"
                : "";

            return $"اشتغل هذا الأسبوع على {stageWord} (معامل تنوّع ×{e.DiversityFactor:0.00})، " +
                   $"وأنتج {e.TotalPieces} قطعة بإجمالي {e.AdjustedWorkdays:0.00} يومية معدّلة حسب صعوبة المراحل، " +
                   $"بحضور {e.PresentDays} يوم{penaltyText} {absenceText}، " +
                   $"فوصلت درجة تقييمه لـ {e.FinalScore:0.00}.";
        }

        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            _onOpenProfile(_workerId);
            Close();
        }

        private void Window_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }

    /// <summary>سطر تفصيل مرحلة واحدة داخل النافذة — تنسيق عرض بس</summary>
    public class StageExplanationRow
    {
        public string ProductName { get; init; } = "";
        public string StageName { get; init; } = "";
        public int PieceCount { get; init; }
        public int PiecesPerWorkday { get; init; }
        public decimal Workdays { get; init; }
        public decimal DifficultyMultiplier { get; init; } = 1.0m;

        public bool HasCustomDifficulty => DifficultyMultiplier != 1.0m;
        public string DifficultyText => HasCustomDifficulty ? $"×{DifficultyMultiplier:0.0#}" : "عادي ×1.0";

        public string ProductStageText => $"{ProductName} — {StageName}";
        public string PieceMathText => $"{PieceCount} قطعة ÷ يومية {PiecesPerWorkday} = {Workdays:0.##} يومية";
    }
}
